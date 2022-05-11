﻿using p4au.modloader.Configuration.Implementation;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace p4au.modloader.Configuration
{
    public class CacheConfig : Configurable<CacheConfig>
    {
        [DisplayName("Cache")]
        [Description("Cache for file merging, please don't touch this")]
        public List<PacInfo> FileCache { get; set; } = new List<PacInfo>();
    }
}
