﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using KSP;
using KSP.Game;
using UnityEngine;
using Newtonsoft.Json;
using SpaceWarp.API.Configuration;
using SpaceWarp.API.Logging;
using SpaceWarp.API.Managers;
using SpaceWarp.API.Mods;
using SpaceWarp.API.Versions;
using SpaceWarp.Patching;
using SpaceWarp.UI;

namespace SpaceWarp.API
{
    /// <summary>
    /// Handles all the SpaceWarp initialization and mod processing.
    /// </summary>
    public class SpaceWarpManager : Manager 
    {
        private BaseModLogger _modLogger;

        private const string MODS_FOLDER_NAME = "Mods";
        public static string MODS_FULL_PATH = Application.dataPath + "/" + MODS_FOLDER_NAME;

        private const string SPACE_WARP_CONFIG_FILE_NAME = "space_warp_config.json";
        private static string SPACEWARP_CONFIG_FULL_PATH = MODS_FULL_PATH + "/" + SPACE_WARP_CONFIG_FILE_NAME;

        public SpaceWarpGlobalConfiguration SpaceWarpConfiguration;

        private readonly List<Mod> _allModScripts = new List<Mod>();
        private readonly List<(string, ModInfo)> _modLoadOrder = new List<(string, ModInfo)>();
        public readonly List<(string,ModInfo)> LoadedMods = new List<(string,ModInfo)>();
        private static readonly List<(string, ModInfo)> AllEnabledModInfo = new List<(string, ModInfo)>();

        protected override void Start()
        {
            base.Start();

            Initialize();
        }

        /// <summary>
        /// Initializes the SpaceWarp manager.
        /// </summary>
        private void Initialize()
        {
            InitializeConfigManager();
            InitializeSpaceWarpConfig();
            
            InitializeModLogger();
            
            InitializePatches();
        }

        ///<summary>
        ///Initializes the configuration manager
        ///</summary>
        public void InitializeConfigManager()
        {
            var confManagerObject = new GameObject("Configuration Manager");
            DontDestroyOnLoad(confManagerObject);
            confManagerObject.AddComponent<ConfigurationManager>();
            confManagerObject.SetActive(true);
        }
        
        
        /// <summary>
        /// Initializes Harmony
        /// </summary>

        private void InitializePatches()
        {
            LoadingScreenPatcher.AddModLoadingScreens();
        }

        /// <summary>
        /// Initializes the SpaceWarp mod logger.
        /// </summary>
        private void InitializeModLogger()
        {
            _modLogger = new ModLogger("Space Warp");
            _modLogger.Info("Warping Spacetime");
        }

        /// <summary>
        /// Read all the mods in the mods path
        /// </summary>
        internal void ReadMods()
        {
            _modLogger.Info("Reading mods");

            string[] modDirectories;
            try
            {
                modDirectories = Directory.GetDirectories(MODS_FULL_PATH);
            }
            catch(Exception exception)
            {
                _modLogger.Critical($"Unable to open mod path: {MODS_FULL_PATH}\nException:{exception}");
                return;
            }

            if (modDirectories.Length == 0)
            {
                _modLogger.Warn("No mods were found! No panic though.");
            }

            foreach (string modFolderuntrimmedU in modDirectories)
            {
                string modFolder = modFolderuntrimmedU.TrimEnd('/', '\\');

                string modName = Path.GetFileName(modFolder);
                if (!File.Exists(modFolder + "\\modinfo.json"))
                {
                    _modLogger.Warn($"Found mod {modName} without modinfo.json");
                    continue;
                }

                if (File.Exists(modFolder + "\\.ignore"))
                {
                    _modLogger.Info($"Skipping mod {modName} due to .ignore file");
                    continue;
                }
                _modLogger.Info($"Found mod: {modName}, adding to enable mods");

                ModInfo info = JsonConvert.DeserializeObject<ModInfo>(File.ReadAllText(modFolder + "\\modinfo.json"));
                string fileName = Path.GetFileName(modFolder);
                AllEnabledModInfo.Add((fileName,info));
            }

            ResolveLoadOrder();
        }

        /// <summary>
        /// Checks if all dependencies are resolved.
        /// </summary>
        /// <param name="mod"></param>
        /// <returns></returns>
        private bool AreDependenciesResolved(ModInfo mod)
        {
            foreach (DependencyInfo dependency in mod.dependencies)
            {
                _modLogger.Info($"{mod.name} dependency - {dependency.id} {dependency.version.min}-{dependency.version.max}");

                string dependencyID = dependency.id;
                SupportedVersionsInfo dependencyVersion = dependency.version;

                bool found = false;
                foreach ((string, ModInfo) loadedMod in _modLoadOrder)
                {
                    if (loadedMod.Item2.mod_id != dependencyID)
                    {
                        continue;
                    }

                    string depLoadedVersion = loadedMod.Item2.version;

                    if (!VersionUtility.IsVersionAbove(depLoadedVersion, dependencyVersion.min)) 
                        return false;
                    if (!VersionUtility.IsVersionBellow(depLoadedVersion, dependencyVersion.max)) 
                        return false;
                        
                    found = true;
                }

                if (!found) return false;
            }

            return true;
        }

        /// <summary>
        /// Resolves mod order
        /// </summary>
        private void ResolveLoadOrder()
        {
            //TODO: Make this way more optimized!
            _modLogger.Info("Resolving Load Order");
            bool changed = true;
            while (changed)
            {
                changed = false;
                List<int> toRemove = new List<int>();
                for (int i = 0; i < AllEnabledModInfo.Count; i++)
                {
                    _modLogger.Info("Attempting to resolve dependencies for " + AllEnabledModInfo[i].Item1);
                    if (AreDependenciesResolved(AllEnabledModInfo[i].Item2))
                    {
                        _modLoadOrder.Add(AllEnabledModInfo[i]);
                        toRemove.Add(i);
                        changed = true;
                    }
                }

                for (int i = toRemove.Count - 1; i >= 0; i--)
                {
                    AllEnabledModInfo.RemoveAt(toRemove[i]);
                }
            }

            if (AllEnabledModInfo.Count > 0)
            {
                foreach ((string modName, ModInfo info) in AllEnabledModInfo)
                {
                    _modLogger.Warn($"Skipping loading of {modName} as not all dependencies could be met");
                }
            }
        }

        /// <summary>
        /// Runs the mod initialization procedures.
        /// </summary>
        internal void InitializeMods()
        {
            _modLogger.Info("Initializing mods");
            

            foreach ((string modName, ModInfo info) in _modLoadOrder)
            {
                string modFolder = MODS_FULL_PATH + "/" + modName;

                _modLogger.Info($"Found mod: {modName}, attempting to load mod");

                // Now we load all assemblies under the code folder of the mod
                string codePath = modFolder + GlobalModDefines.BINARIES_FOLDER;

                if (Directory.Exists(codePath))
                {
                    if (!TryLoadMod(codePath, modName, out Type mainModType))
                    {
						// error logging is done inside TryLoadMod
						continue;
                    }

                    InitializeModObject(modName, info, mainModType);
                }
                else
                {
                    _modLogger.Error($"Directory not found: {codePath}");
                }
            }
        }

        /// <summary>
        /// Tries to load a mod at a path
        /// </summary>
        /// <param name="codePath">The full path to this mod binaries.</param>
        /// <param name="modName">The mod name</param>
        /// <param name="mainModType">The Mod type found</param>
        /// <returns>If the mod was successfully found.</returns>
        private bool TryLoadMod(string codePath, string modName, out Type mainModType)
        {
            string[] files;
            try
            {
                files = Directory.GetFiles(codePath);
			}
            catch
            {
                _modLogger.Error($"Could not load mod: {modName}, unable to read directory");
                mainModType = null;
				return false;
            }

            List<Assembly> modAssemblies = new List<Assembly>();
            foreach (string file in files)
            {
                // we only want to load dll files, ignore everything else
                if (!file.EndsWith(".dll"))
                {
                    _modLogger.Warn($"Non-dll file found in \"{codePath}\" \"{file}\", Ignoring");
					continue;
				}

                Assembly asm;
                try
                {
					asm = Assembly.LoadFrom(file);
				}
                catch(Exception exeption)
                {
                    _modLogger.Error($"Could not load mod: {modName}, Failed to load assembly {file}\nException: {exeption}");
                    mainModType = null;

                    return false;
                }

				modAssemblies.Add(asm);
            }

            mainModType = null;
            foreach (Assembly asm in modAssemblies)
            {
                mainModType = asm.GetTypes().FirstOrDefault(type => type.GetCustomAttribute<MainModAttribute>() != null);
                if (mainModType != null) break;
            }

            if (mainModType == null)
            {
                _modLogger.Error($"Could not load mod: {modName}, no type with [MainMod] exists");
                return false;
            }

            
            // We want to load the configuration for the mod as well
            Type configurationModType = null;
            foreach (Assembly asm in modAssemblies)
            {
                configurationModType = asm.GetTypes()
                    .FirstOrDefault(type => type.GetCustomAttribute <ModConfigAttribute>() != null);
                if (configurationModType != null) break;
            }

            if (configurationModType != null)
            {
                InitializeModConfig(configurationModType, modName);
            }

            if (!typeof(Mod).IsAssignableFrom(mainModType))
            {
                _modLogger.Error($"Could not load mod: {modName}, the found class ({mainModType.FullName}) with [MainMod] doesn't inherit from {nameof(Mod)}");

                mainModType = null;
				return false;
            }

            return true;

        }

        /// <summary>
        /// Tries to find a specific mods config file, if none is found, one is created
        /// </summary>
        private void InitializeModConfig(Type config_type, string mod_id)
        {
            object modConfiguration = null;
            var config_path = MODS_FULL_PATH + "\\" + mod_id + "\\config\\config.json";
            if (!File.Exists(config_path))
            {
                modConfiguration = Activator.CreateInstance(config_type);
                foreach (var fieldInfo in config_type.GetFields(BindingFlags.Instance | BindingFlags.Public))
                {
                    var def = fieldInfo.GetCustomAttribute<ConfigDefaultValueAttribute>();
                    if (def != null)
                    {
                        fieldInfo.SetValue(modConfiguration, def.DefaultValue);
                    }
                }
            }
            else
            {
                try
                {
                    string json = File.ReadAllText(config_path);
                    modConfiguration = JsonConvert.DeserializeObject(json,config_type);
                    
                }
                catch (Exception exception)
                {
                    _modLogger.Error($"Loading mod config failed\nException: {exception}");

                    File.Delete(config_path);
                    InitializeSpaceWarpConfig();
                    return;
                }
            }

            try
            {
                File.WriteAllLines(config_path, new[] { JsonConvert.SerializeObject(modConfiguration) });
            }
            catch (Exception exception)
            {
                _modLogger.Error($"Saving mod config failed\nException: {exception}");
            }

            if (ManagerLocator.TryGet(out ConfigurationManager configurationManager))
            {
                configurationManager.Add(mod_id,(config_type,modConfiguration,config_path));
            }
        }
        
        /// <summary>
        /// Tried to find the SpaceWarp config file in the game, if none is found one is created.
        /// </summary>
        /// <param name="spaceWarpGlobalConfiguration"></param>
        private void InitializeSpaceWarpConfig()
        {
            if (!File.Exists(SPACEWARP_CONFIG_FULL_PATH))
            {
                SpaceWarpConfiguration = new SpaceWarpGlobalConfiguration();
                SpaceWarpConfiguration.ApplyDefaultValues();
            }
            else
            {
                try
                {
                    string json = File.ReadAllText(SPACEWARP_CONFIG_FULL_PATH);
                    SpaceWarpConfiguration = JsonConvert.DeserializeObject<SpaceWarpGlobalConfiguration>(json);
                }
                catch (Exception exception)
                {
                    //TODO: log this in a nicer way, for now I guess we can just construct a new logger
                    new ModLogger("Space Warp").Error($"Loading space warp config failed\nException: {exception}");

                    File.Delete(SPACEWARP_CONFIG_FULL_PATH);
                    InitializeSpaceWarpConfig();
                    return;
                }
            }
            
            try
            {
				File.WriteAllLines(SPACEWARP_CONFIG_FULL_PATH, new[] { JsonConvert.SerializeObject(SpaceWarpConfiguration) });
			}
            catch(Exception exception)
            {
                //TODO: log this in a nicer way, for now I guess we can just construct a new logger
                new ModLogger("Space Warp").Error($"Saving the spacewarp config failed\nException: {exception}");
			}
        }

        /// <summary>
        /// Initializes a mod object.
        /// </summary>
        /// <param name="modName">The mod name to initialize.</param>
        /// <param name="mainModType">The mod type to initialize.</param>
        /// <param name="newModLogger">The new mod logger to spawn</param>
        private void InitializeModObject(string modName, ModInfo info, Type mainModType)
        {
            GameObject modObject = new GameObject($"Mod: {modName}");
            Mod modComponent = (Mod)modObject.AddComponent(mainModType);
            
            _allModScripts.Add(modComponent);
            
            modComponent.Setup(transform.parent, info);
            modObject.SetActive(true);

            _modLogger.Info($"Loaded: {modName}");

			// we probably dont want to completely stop loading mods if 1 mod throws an exception on Initialize
			try
			{
				modComponent.Initialize();
			}
            catch(Exception exception)
            {
                _modLogger.Critical($"Exception in {modName} Initialize(): {exception}");
            }

            LoadedMods.Add((modName, info));
        }

        /// <summary>
        /// Calls the OnInitialized method on each initialized mod.
        /// </summary>
        internal void InvokePostInitializeModsAfterAllModsLoaded()
        {
            foreach (Mod mod in _allModScripts)
            {
                try
                {
					mod.OnInitialized();
				}
                catch(Exception exception)
                {
					_modLogger.Critical($"Exception in {mod.name} AfterInitialization(): {exception}");
				}
            }
            InitModUI();
        }
        /// <summary>
        /// Initializes the UI for the mod list and configuration menu
        /// </summary>
        private void InitModUI()
        {
            GameObject modUIObject = new GameObject();
            DontDestroyOnLoad(modUIObject);
            modUIObject.transform.SetParent(transform.parent);
            ModListUI UI = modUIObject.AddComponent<ModListUI>();
            UI.manager = this;
            
            modUIObject.SetActive(true);
        }
    }
}