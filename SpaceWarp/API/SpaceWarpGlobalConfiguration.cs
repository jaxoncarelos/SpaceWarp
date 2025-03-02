﻿using System.ComponentModel;
using Newtonsoft.Json;

namespace SpaceWarp.API
{
    [JsonObject(MemberSerialization.OptIn)]
    public class SpaceWarpGlobalConfiguration
    {
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)] 
        [DefaultValue((int)Logging.LogLevel.Info)] 
        public int LogLevel { get; set; }

        public void ApplyDefaultValues()
        {
            LogLevel = (int)Logging.LogLevel.Info;
        }
    }
}