﻿using System.Collections.Generic;
using Newtonsoft.Json;
namespace SpaceWarp.API.Configuration
{
    /// <summary>
    /// Representation of the mod info JSON file.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class ModInfo
    {
        [JsonProperty("mod_id")] 
        public string mod_id { get; set; }

        [JsonProperty("name")]
        public string name { get; set; }

        [JsonProperty("author")] 
        public string author { get; set; }
        
        [JsonProperty("description")] 
        public string description { get; set; }
        
        [JsonProperty("source")] 
        public string source { get; set; }

        [JsonProperty("version")] 
        public string version { get; set; }

        [JsonProperty("dependencies")] 
        public List<DependencyInfo> dependencies { get; set; }

        [JsonProperty("ksp2_version")] 
        public SupportedVersionsInfo supported_ksp2_versions { get; set; }
    }
}