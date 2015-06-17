using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Umbraco.Core;
using Umbraco.Core.Models;
using Umbraco.Core.Services;
using Umbraco.Core.Logging;
using jumps.umbraco.usync.helpers;

namespace jumps.umbraco.usync.Extensions
{
    /// <summary>
    /// Does the stuff we need to help up export/import content Types
    /// (DocumentTypes in this instance)
    /// </summary>
    public static class IContentTypeExtensions
    {
        static IPackagingService _packageService;
        static IContentTypeService _contentTypeService;
        static IDataTypeService _dataTypeService;

        static IContentTypeExtensions()
        {
            _packageService = ApplicationContext.Current.Services.PackagingService;
            _contentTypeService = ApplicationContext.Current.Services.ContentTypeService;
            _dataTypeService = ApplicationContext.Current.Services.DataTypeService;
        }

        public static XElement ExportToXml(this IContentType item)
        {
            XElement element = XmlDoc.CloneElement(_packageService.Export(item));

            // put the sort order on the tabs in a way CodeGen can parse
            // Umbraco's export adds the SortOrder and CodeGen wants Order, so we have both
            var tabs = element.Element("Tabs");
            foreach (var tab in item.PropertyGroups)
            {
                XElement tabNode = tabs.Elements().First(x => x.Element("Id").Value == tab.Id.ToString());

                if ( tabNode != null)
                {
                    if (!tabNode.Descendants("Order").Any())
                        tabNode.Add(new XElement("Order", tab.SortOrder));
                }
            }

            return element;
        }

        public static IEnumerable<IContentType> ImportContentType(this XElement node)
        {
            XElement info = node.Element("Info");
            var master = info.Element("Master");

            // if parent node is null, remove
            var newParentAlias = master != null ? master.Value : "";

            if (newParentAlias.Length == 0) master.Remove();

            IEnumerable<IContentType> imported = _packageService.ImportContentTypes(node, false, raiseEvents: false);


            if (newParentAlias.Length > 0)
                SetParent(imported.Single(), newParentAlias);

            return imported; 

        }

        public static void SetParent(IContentType t, string parentAlias)
        {
            if (string.IsNullOrWhiteSpace(parentAlias)) t.ParentId = 0;
            var oldParentId = t.ParentId;
            if (oldParentId == -1) return;

            var pt = _contentTypeService.GetContentType(parentAlias);
            if (t.ParentId == pt.Id) return;

            t.ParentId = pt.Id;
            t.Path = pt.Path + "," + t.Id;
            _contentTypeService.Save(t);
        }


        /*
         * Import Part 2 - these functions all do post import 2nd pass 
         * tidy up stuff.
         */

        public static void ImportStructure(this IContentType item, XElement node)
        {
            XElement structure = node.Element("Structure");

            List<ContentTypeSort> allowed = new List<ContentTypeSort>();
            int sortOrder = 0;

            foreach (var doctype in structure.Elements("DocumentType"))
            {
                string alias = doctype.Value;

                if (!string.IsNullOrEmpty(alias))
                {
                    IContentType aliasDoc = _contentTypeService.GetContentType(alias);

                    if (aliasDoc != null)
                    {
                        allowed.Add(new ContentTypeSort(
                            new Lazy<int>(() => aliasDoc.Id), sortOrder, aliasDoc.Name));
                        sortOrder++;
                    }
                }
            }
            item.AllowedContentTypes = allowed;
        }

        public static void ImportTabSortOrder(this IContentType item, XElement node)
        {
            XElement tabs = node.Element("Tabs");

            foreach (var tab in tabs.Elements("Tab"))
            {
                // if we have CodeGen def's, id is always 0, use caption
                var tabName = tab.Element("Caption").Value;
                var sortOrder = tab.Element("Order") ?? tab.Element("SortOrder");
                
                if (sortOrder != null)
                {
                    if ( !String.IsNullOrEmpty(sortOrder.Value))
                    {
                        var itemTab = item.PropertyGroups.FirstOrDefault(x => x.Name == tabName);
                        if (itemTab != null)
                        {
                            itemTab.SortOrder = int.Parse(sortOrder.Value);
                        }
                    }
                }
            }
        }

        public static void UpdateGroupParentIds(this IContentType item)
        {
            var parent = _contentTypeService.GetContentType(item.ParentId);
            if (parent != null)
            {
                foreach (var tab in item.PropertyGroups)
                {
                    // check immediate parent
                    var parentTab = (parent.CompositionPropertyGroups.Where(x => x.Name == tab.Name && x.ParentId==null)).Distinct<PropertyGroup>()
                            .Select(x => x.Id)
                            .SingleOrDefault();
                    // check ancestors
                    if (parentTab > 0)
                    {
                        tab.ParentId = parentTab;
                    }
                }
            }
        }

        public static void ImportRemoveMissingProps(this IContentType item, XElement node)
        {
            // LC - check moved to caller
            // don't do this if the setting is set to false
            //if (!uSyncSettings.docTypeSettings.DeletePropertyValues)
            //{
            //    return;
            //}

            List<string> propertiesToRemove = new List<string>();
            Dictionary<string, string> propertiesToMove = new Dictionary<string, string>();

            // go through the properties in the item
            foreach (var property in item.PropertyTypes.ToList())
            {
                // is this property in the xml ?
                XElement propertyNode = node.Element("GenericProperties")
                    .Elements("GenericProperty")
                    .Where(x => x.Element("Alias").Value == property.Alias)
                    .SingleOrDefault();

                if (propertyNode == null)
                {
                    LogHelper.Info<uSync>("Removing {0} from {1}", () => property.Alias, () => item.Name);
                    propertiesToRemove.Add(property.Alias);
                }
                else
                {
                    // at this point we write our properties over those 
                    // in the db - because the import doesn't do this 
                    // for existing items.
                    LogHelper.Debug<uSync>("Updating prop {0} for {1}", () => property.Alias, () => item.Alias);
                    var editorAlias = propertyNode.Element("Type").Value;


                    property.Name = propertyNode.Element("Name").Value;
                    property.Description = propertyNode.Element("Description").Value;
                    property.Mandatory = propertyNode.Element("Mandatory").Value.ToLowerInvariant().Equals("true");
                    property.ValidationRegExp = propertyNode.Element("Validation").Value;

                    XElement sortOrder = propertyNode.Element("Order") ?? propertyNode.Element("SortOrder");
                    if (sortOrder != null)
                        property.SortOrder = int.Parse(sortOrder.Value);

                    var tab = propertyNode.Element("Tab").Value;
                    if (!string.IsNullOrEmpty(tab))
                    {
                        var added = false;
                        if (!item.PropertyGroups.Select(x => x.Name).Contains(tab))
                        {
                            added = item.AddPropertyGroup(tab);
                            if (added) _contentTypeService.Save(item);
                        }
                        
                    }

                    var propGroup = item.PropertyGroups.FirstOrDefault(x => x.Name == tab);
                    // we have a group and it does not contain our current property
                    if (propGroup.PropertyTypes.All(x => x.Alias != property.Alias))
                    {
                        // if it's not in this prop group - we can move it it into it
                        LogHelper.Info<uSync>("Moving {0} in {1} to {2}",
                            () => property.Alias, () => item.Name, () => tab);
                        propertiesToMove.Add(property.Alias, tab);
                    }
                }
            }

            foreach (string alias in propertiesToRemove)
            {
                LogHelper.Debug<uSync>("Removing {0}", () => alias);
                item.RemovePropertyType(alias);

                // if slow - save after every remove 
                // on big sites, this increases the chances of your SQL server completing the operation in time...
                // 
                /*
                if (uSyncSettings.SlowUpdate)
                {
                    LogHelper.Debug<uSync>("SlowMode: saving now {0}", () => item.Name);
                    _contentTypeService.Save(item);
                }
                 */
            }

            foreach (KeyValuePair<string, string> movePair in propertiesToMove)
            {
                LogHelper.Info<uSync>("Moving {0} in {1} to {2}",
                    () => movePair.Key, () => item.Name, () => movePair.Value); 
                item.MovePropertyType(movePair.Key, movePair.Value);
            }

            var removedGroups = 0;
            foreach (var group in item.PropertyGroups.Where(x => !x.PropertyTypes.Any()).ToList())
            {
                item.RemovePropertyGroup(group.Name);
                removedGroups++;
            }

            if (propertiesToRemove.Count > 0 || propertiesToMove.Count > 0 || removedGroups > 0)
            {
                LogHelper.Debug<uSync>("Saving {0}", () => item.Name);

                _contentTypeService.Save(item);
            }
        }

        public static void UpdatePropertyTypes(this IContentType item, XElement node)
        {
            var initialProperties = item.PropertyTypes;
            var newProperties =
                node.Descendants("GenericProperties")
                    .Descendants("GenericProperty")
                    .ToDictionary(x => x.Descendants("Alias").Single().Value,
                        y => y.Descendants("Type").Single().Value);
            try
            {
                var propsToChange =
                    initialProperties.Where(
                        x => newProperties.Keys.Contains(x.Alias) &&
                             x.PropertyEditorAlias != newProperties.Single(y => y.Key == x.Alias).Value);
                foreach (var p in propsToChange)
                {
                    var newPropertyInfo = newProperties.Single(x => x.Key == p.Alias);
                    var dt =
                        _dataTypeService
                            .GetAllDataTypeDefinitions().Single(x => x.PropertyEditorAlias == newPropertyInfo.Value);
                    LogHelper.Info<uSync>(string.Format("Setting {0}:{1}:  {2}->{3}.", item.Name, p.Alias, p.PropertyEditorAlias,
                        dt.Name));
                    p.DataTypeDefinitionId = dt.Id;
                }

                _contentTypeService.Save(item);
            }
            catch (InvalidOperationException ex)
            {
                LogHelper.Error<uSync>(string.Format("Unable to update properties on: {0}", item.Alias), ex);
            }

        }

        public static void ImportContainerType(this IContentType item, XElement node)
        {
            XElement Info = node.Element("Info");

            if (Info != null)
            {
                XElement container = Info.Element("Container");
                if (container != null)
                {
                    bool isContainer = false;
                    bool.TryParse(container.Value, out isContainer);
                    item.IsContainer = isContainer;
                }
            }
        }

        public static string GetSyncPath(this IContentType item)
        {
            string path = "";

            if (item != null)
            {
                if (item.ParentId != 0)
                {
                    path = _contentTypeService.GetContentType(item.ParentId).GetSyncPath();
                }
                path = string.Format("{0}\\{1}", path, helpers.XmlDoc.ScrubFile(item.Alias));
            }
            return path;
        }
    }
}