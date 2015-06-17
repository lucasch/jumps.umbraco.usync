using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Umbraco.Core;
using Umbraco.Core.Services;
using Umbraco.Core.Models;

using System.IO;

using System.Xml;
using System.Xml.Linq;

using Umbraco.Core.Logging;
using System.Security.Cryptography;

using umbraco.cms.businesslogic.web;

using jumps.umbraco.usync.Extensions;
using System.Diagnostics;
using System.Runtime.Remoting.Messaging;


namespace jumps.umbraco.usync.helpers
{
    /// <summary>
    /// tracks the updates (where it can) so you can
    /// only run the changes where they might have happened
    /// </summary>
    public static class Tracker
    {
        private static IFileService _fileService;
        private static IContentTypeService _contentTypeService;
        private static IPackagingService _packagingService;
        private static IDataTypeService _dataTypeService;

        private static Dictionary<Guid, IDataTypeDefinition> _dataTypes;
        
        static Tracker()
        {
            _fileService = ApplicationContext.Current.Services.FileService;
            _contentTypeService = ApplicationContext.Current.Services.ContentTypeService;
            _dataTypeService = ApplicationContext.Current.Services.DataTypeService;
            _packagingService = ApplicationContext.Current.Services.PackagingService;
        }

        public static bool ContentTypeChanged(XElement node)
        {
            string filehash = XmlDoc.GetPreCalculatedHash(node);
            if (string.IsNullOrEmpty(filehash))
                return true;

            XElement aliasElement = node.Element("Info").Element("Alias");
            if (aliasElement == null)
                return true;

            //var _contentService = ApplicationContext.Current.Services.ContentTypeService;
            var item = _contentTypeService.GetContentType(aliasElement.Value);

            if (item == null) // import because it's new. 
                return true;

            XElement export = item.ExportToXml();

            string dbMD5 = XmlDoc.CalculateMD5Hash(export);

            // XmlDoc.SaveElement("doctmp", item.Alias, export);

            return (!filehash.Equals(dbMD5));
        }

        #region Public Granular Change trackers
        
        public static bool CodeGenContentTypeChanged(XElement node)
        {
            var props = true;
            var info = true;
            var structure = true;
            var parent = true;
            var name = node.Element("Info").Element("Alias").Value;
            IContentType c = _contentTypeService.GetContentType(name);
            if (c == null) return true;

            var second = c.ExportToXml().ToCodeGen();

            return (
                   (info = InfoChanged(second, node)) ||
                   (props = PropertiesChanged(second, node)) ||
                   (structure = StructureChanged(second, node)) ||
                   (parent = ParentChanged(c, node)));
        }
        public static bool PropertiesChanged(IContentType c, XElement node)
        {
            if (c == null) return true;
            var second = c.ExportToXml();
            return PropertiesChanged(node, second);
        }
        public static bool PropertiesChanged(IMediaType c, XElement node)
        {
            if (c == null) return true;
            var second = c.ExportToXml();
            return PropertiesChanged(node, second);
        }
        public static bool InfoChanged(IContentType c, XElement node)
        {
            if (c == null) return true;
            var second = c.ExportToXml();
            return InfoChanged(node, second);
        }
        public static bool InfoChanged(IMediaType c, XElement node)
        {
            if (c == null) return true;
            var second = c.ExportToXml();
            return InfoChanged(node, second);
        }
        public static bool StructureChanged(IContentType c, XElement node)
        {
            if (c == null) return true;
            var second = c.ExportToXml();
            return StructureChanged(node, second);
        }
        public static bool StructureChanged(IMediaType c, XElement node)
        {
            if (c == null) return true;
            var second = c.ExportToXml();
            return StructureChanged(node, second);
        }
        public static bool ParentChanged(IContentType c, XElement node)
        {
            if (c == null) return true;
            var parent = _contentTypeService.GetContentType(c.ParentId);
            return ParentChanged(node, parent);
        }
        public static bool ParentChanged(IMediaType c, XElement node)
        {
            if (c == null) return true;
            var parent = _contentTypeService.GetMediaType(c.ParentId);
            return ParentChanged(node, parent);
        }
        
        #endregion

        #region Private Granular Change trackers
        
        private static bool PropertiesChanged(XElement first, XElement second)
        {
            var properties = first.Element("GenericProperties");
            var tabs = first.Element("Tabs");
            var itemProps = second.Element("GenericProperties");
            var itemTabs = second.Element("Tabs");

            //compare properties
            if (itemProps.DescendantNodes().Count() != properties.DescendantNodes().Count()) return true;

            foreach (var property in properties.Descendants("GenericProperty"))
            {
                var propertyAlias = property.Element("Alias").Value;
                var ptype = property.Element("Type").Value;
                XElement matching =
                    itemProps.Descendants("GenericProperty")
                        .FirstOrDefault(p => p.Element("Alias").Value == propertyAlias && p.Element("Type").Value == ptype);

                if (matching==null|| XmlDoc.CalculateMD5Hash(property) != XmlDoc.CalculateMD5Hash(matching)) 
                    return true;
            }


            //compare tabs
            foreach (var tab in tabs.Descendants("Tab"))
            {
                // we know Id will be off here, we don't care
                var targetTab = itemTabs.Descendants("Tab").SingleOrDefault(e=>e.Element("Caption").Value== tab.Element("Caption").Value);
                tab.SetElementValue("Id", 0);
                if (targetTab != null)
                {
                    targetTab.SetElementValue("Id", 0);
                    if (NodeChanged(tab, targetTab))
                        return true;
                }
            }

            return false;
        }
        private static bool InfoChanged(XElement first, XElement second)
        {
            return NodeChanged(first.Element("Info"), second.Element("Info"));
        }
        private static bool StructureChanged(XElement first, XElement second)
        {
            var itemStructure = second.Element("Structure");
            var structure = first.Element("Structure");
            var changed = NodeChanged(structure, itemStructure) || 
                          structure.Descendants().Count()!=itemStructure.Descendants().Count();
            return changed;
        }
        private static bool ParentChanged(XElement node, IContentTypeBase parent)
        {
            var p = node.Element("Info").Element("Master").Value;
            var pAlias = "";
            if (parent != null) pAlias = parent.Alias;

            return p != pAlias;
        }
        private static bool NodeChanged(XElement source, XElement target)
        {

            //var diff = from s in source.Descendants()
            //    from t in target.Descendants()
            //    where s.Name == t.Name && s.Value != t.Value
            //    select NodeChanged(s, t);
            
            //return diff.Any();

            if (target == null && !string.IsNullOrWhiteSpace(source.Value)) 
                return true;

            foreach (var elem in source.Descendants())
            {
                var matching = target.Descendants().SingleOrDefault(e => e.Name == elem.Name && e.Value == elem.Value);
                if (matching == null && !string.IsNullOrWhiteSpace(elem.Value))
                    return true;
                
                if (elem.HasElements)               
                {
                    if (NodeChanged(elem, matching))
                    {
                        return true;
                    }
                } else {
                    if (!string.IsNullOrWhiteSpace(elem.Value) && elem.Value != matching.Value)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        
        #endregion
        
        public static bool DataTypeChanged(XElement node)
        {
            string filehash = XmlDoc.GetPreCalculatedHash(node);
            if (string.IsNullOrEmpty(filehash))
                return true;

            var dataTypeDefinitionId = new Guid(node.Attribute("Definition").Value);

            XAttribute defId = node.Attribute("Definition");
            if (defId == null)
                return true;
            /*
            //var _dataTypeService = ApplicationContext.Current.Services.DataTypeService;
            var item = _dataTypeService.GetDataTypeDefinitionById(new Guid(defId.Value));
            */
            if ( _dataTypes == null )
            {
                // speed test, calling data types seems slow, 
                // so we load all them at once, then refrence this when doing the compares.
                // this is a little bit faster than calling each one as we go through...            
                _dataTypes = new Dictionary<Guid, IDataTypeDefinition>();
                foreach (IDataTypeDefinition dtype in _dataTypeService.GetAllDataTypeDefinitions())
                {
                    _dataTypes.Add(dtype.Key, dtype);
                }

            }

            Guid defGuid = new Guid(defId.Value);
            if (!_dataTypes.ContainsKey(defGuid) )
                return true;

            //var packagingService = ApplicationContext.Current.Services.PackagingService;
            XElement export = _packagingService.Export(_dataTypes[defGuid], false);
            string dbMD5 = XmlDoc.CalculateMD5Hash(export, true);

            // LogHelper.Info<uSync>("XML File (we just got to hash from) {0}", () => export.ToString());
            // LogHelper.Info<uSync>("File {0} : Guid {1}", () => filehash, () => dbMD5);

            return (!filehash.Equals(dbMD5));

        }

        public static bool TemplateChanged(XElement node)
        {
            string filehash = XmlDoc.GetPreCalculatedHash(node);
            if (string.IsNullOrEmpty(filehash))
                return true;

            XElement alias = node.Element("Alias");
            if (alias == null)
                return true;

            //var _fileService = ApplicationContext.Current.Services.FileService;
            var item = _fileService.GetTemplate(alias.Value);
            if (item == null)
                return true;

            // for a template - we never change the contents - lets just md5 the two 
            // properties we care about (and save having to load the thing from disk?

            string values = item.Alias + item.Name;
            string dbMD5 = XmlDoc.CalculateMD5Hash(values);

            return (!filehash.Equals(dbMD5));

        }

        public static bool StylesheetChanges(XmlDocument xDoc)
        {
            XElement node = XElement.Load(new XmlNodeReader(xDoc));

            string filehash = XmlDoc.GetPreCalculatedHash(node);
            if (string.IsNullOrEmpty(filehash))
                return true;

            XElement name = node.Element("Name");
            if (name == null)
                return true;

            var item = StyleSheet.GetByName(name.Value);
            if (item == null)
                return true;

            XmlDocument xmlDoc = helpers.XmlDoc.CreateDoc();
            xmlDoc.AppendChild(item.ToXml(xmlDoc));
            string dbMD5 = XmlDoc.CalculateMD5Hash(xmlDoc);

            return (!filehash.Equals(dbMD5));
        }

        // remove known missing properties from def's to fall in line w/CodeGen
        private static XElement ToCodeGen(this XElement source)
        {
            //to remove: 
            //  <IsListView />
            //  <Compositions />

            source.Element("Info").Element("IsListView").Remove();
            source.Element("Info").Element("Compositions").Remove();
            return source;
        }

        public static bool IsCodeGen(this XElement source)
        {
            return !source.Element("Info").Descendants("Composition").Any();
        }
    }
}
