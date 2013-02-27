﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Configuration;

namespace jumps.umbraco.usync
{
    /// <summary>
    /// uSync Settings - 
    /// 
    /// reads the uSync bit of the Web.Config
    /// 
    /// <uSync>
    ///     <Settings>
    ///         <add 
    ///             read="true"                     - read the uSync directory on startup
    ///             write="false"                   - write the uSync directory on statup
    ///             attach="true"                   - attach the events to save on the fly
    ///             folder="~/uSync/"               - place to put files
    ///             archive="~/uSync.Archive/"      - place to archive files
    ///             versions="true"                 - store versions at every save
    ///             />
    ///     </settings>
    /// </uSync>
    /// 
    /// </summary>
    public class uSyncSettings : ConfigurationSection
    {
        [ConfigurationProperty("read", DefaultValue = "true", IsRequired = false)]
        public Boolean Read
        {
            get
            {
                return (Boolean)this["read"];
            }
            set
            {
                this["read"] = value;
            }
        }

        [ConfigurationProperty("write", DefaultValue = "false", IsRequired = false)]
        public Boolean Write
        {
            get
            {
                return (Boolean)this["write"];
            }
            set
            {
                this["write"] = value;
            }
        }

        [ConfigurationProperty("attach", DefaultValue = "true", IsRequired = false)]
        public Boolean Attach
        {
            get
            {
                return (Boolean)this["attach"];
            }
            set
            {
                this["attach"] = value;
            }
        }

        [ConfigurationProperty("folder", DefaultValue = "~/uSync/", IsRequired = false)]
        public String Folder
        {
            get
            {
                return (String)this["folder"];
            }
            set
            {
                this["folder"] = value;
            }
        }

        [ConfigurationProperty("archive", DefaultValue = "~/uSync.archive/", IsRequired = false)]
        public String Archive
        {
            get
            {
                return (String)this["archive"];
            }
            set
            {
                this["archive"] = value;
            }
        }

        [ConfigurationProperty("versions", DefaultValue = "true", IsRequired = false)]
        public Boolean Versions
        {
            get
            {
                return (Boolean)this["versions"];
            }
            set
            {
                this["versions"] = value;
            }
        }



    }
}