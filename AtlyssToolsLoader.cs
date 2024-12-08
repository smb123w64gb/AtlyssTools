using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AtlyssTools.Commands;
using AtlyssTools.Registries;
using AtlyssTools.Utility;
using BepInEx;
using JetBrains.Annotations;
using Newtonsoft.Json;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AtlyssTools;

public class AtlyssToolsLoader
{
    public readonly Dictionary<string, AtlyssToolsLoaderModInfo> ModInfos = new();

    private readonly LoaderStateMachine _stateMachine = new();

    public class AtlyssLoaderStateManager : LoaderStateManager
    {
        public void PreLibraryInit()
        {
            foreach (var modInfo in Instance.ModInfos.Values)
            {
                foreach (var action in modInfo.OnPreLibraryInit)
                {
                    action?.Invoke();
                }
            }
        }

        public void PostLibraryInit()
        {
            foreach (var modInfo in Instance.ModInfos.Values)
            {
                foreach (var action in modInfo.OnPostLibraryInit)
                {
                    action?.Invoke();
                }
            }
        }

        public void PreCacheInit()
        {
            Instance.LoadAllJsonAssets();
            foreach (var modInfo in Instance.ModInfos.Values)
            {
                foreach (var action in modInfo.OnPreCacheInit)
                {
                    action?.Invoke();
                }
            }
        }

        public void PostCacheInit()
        {
            // load all json assets
            foreach (var modInfo in Instance.ModInfos.Values)
            {
                foreach (var action in modInfo.OnPostCacheInit)
                {
                    action?.Invoke();
                }
            }
        }
    }

    private readonly Dictionary<System.Type, BaseScriptablesManager> _managers;

    public void RegisterManagers()
    {
        List<BaseScriptablesManager> managers =
        [
            StatusConditionManager.Instance, SceneTransferConditionManager.Instance, PolymorphConditionManager.Instance,
            ChestpieceManager.Instance, ArmorDyeManager.Instance, CapeManager.Instance, ClassTomeManager.Instance,
            HelmManager.Instance, LeggingsManager.Instance, RingManager.Instance, ShieldManager.Instance,
            SkillScrollManager.Instance, StatusConsumableManager.Instance, TradeItemManager.Instance,
            WeaponManager.Instance,
            CreepManager.Instance, QuestManager.Instance, PlayerRaceManager.Instance, CombatElementManager.Instance,
            StatModifierManager.Instance, PlayerBaseClassManager.Instance, SkillManager.Instance,
            ArmorRenderManager.Instance, Registries.ShopkeepManager.Instance, CastEffectCollectionManager.Instance
        ];

        foreach (var manager in managers)
        {
            _managers.Add(manager.GetObjectType(), manager);
            _stateMachine.RegisterManager(manager.GetStateManager());
        }
        
        // add our attribute managers
        _attributeManagers.Add(new CommandManager());
        _attributeManagers.Add(new ChatProcessorManager());
    }

    private readonly List<IAttributeRegisterableManager> _attributeManagers = new();

    AtlyssToolsLoader()
    {
        _managers = new();
        RegisterManagers();

        _stateMachine.RegisterManager(new AtlyssLoaderStateManager());
    }

    public BaseScriptablesManager GetManager(System.Type type)
    {
        if (_managers.TryGetValue(type, out var manager))
        {
            return manager;
        }

        return null;
    }

    public LoaderStateMachine.LoadState State
    {
        get => _stateMachine.State;
        set => _stateMachine.SetState(value);
    }

    private void FindAttributeLoaded(Assembly assembly)
    {
        foreach (System.Type type in assembly.GetTypes())
        {
            // skip abstract classes/interfaces
            if (type.IsAbstract || type.IsInterface)
            {
                continue;
            }
            
            
            foreach (IAttributeRegisterableManager attributeManager in _attributeManagers)
            {
                if (attributeManager.CanRegister(type))
                {
                    attributeManager.Register(type, assembly.GetName().Name);
                }
            }
        }
    }
    
    public static void LoadPlugin(string modName, string modPath, Assembly assembly)
    {
        modName = modName.ToLower();
        if (assembly != null)
        {
            Instance.FindAttributeLoaded(assembly); //may be a asset only mod
        }

        AtlyssToolsLoaderModInfo modInfo;
        if (Instance.ModInfos.ContainsKey(modName)) // i don't know why we'd reload a mod, but we can
        {
            modInfo = Instance.ModInfos[modName];
        }
        else
        {
            modInfo = new() { ModId = modName, ModPath = modPath };
            Instance.ModInfos.Add(modName, modInfo);
        }

        modInfo.Initialize();

        if (!Directory.Exists(modPath + "/Assets"))
        {
            Plugin.Logger.LogError($"Mod {modName} does not have an Assets folder");
        }

        // find all files in ModPath/Assets, if it doesn't end in .manifest
        modInfo.LoadAssetBundles();
        // delay loading jsons until after asset bundles are loaded
        
        // now initialize the mod
        Plugin.Logger.LogInfo($"Loaded AtlyssTools mod {modName}");
    }
    
    public static void LoadPlugin(string modName, string modPath)
    {
        LoadPlugin(modName, modPath, Assembly.GetCallingAssembly());
    }

    public static void UnloadMod(string modName)
    {
        if (!Instance.ModInfos.ContainsKey(modName))
        {
            return;
        }

        Instance.ModInfos[modName].Dispose();
        Instance.ModInfos.Remove(modName);
    }

    public static List<T> GetScriptableObjects<T>() where T : ScriptableObject
    {
        List<T> objects = new();
        foreach (var modInfo in Instance.ModInfos.Values)
        {
            objects.AddRange(modInfo.GetModScriptableObjects<T>());
        }

        return objects;
    }

    public static List<T> GetScriptableObjects<T>(string modName) where T : ScriptableObject
    {
        if (!Instance.ModInfos.ContainsKey(modName))
        {
            return null;
        }

        return Instance.ModInfos[modName].GetModScriptableObjects<T>();
    }

    public static T LoadAsset<T>(string assetName) where T : Object
    {
        if (string.IsNullOrEmpty(assetName))
        {
            return null;
        }

        // replace \\ with / for windows
        assetName = assetName.Replace("\\", "/");

        // we use : to mark a specific mod's assets. default means to return from Resources
        if (assetName.Contains(":"))
        {
            string[] parts = assetName.Split(':');
            if (parts.Length != 2)
            {
                Plugin.Logger.LogError($"Failed to load {assetName} of type {typeof(T).Name} Invalid format");
                return null;
            }

            T returnV =  Instance.ModInfos[parts[0].ToLower()].LoadModAsset<T>(parts[1]);
            
            if (returnV != null)
            {
                return returnV;
            }
            
            Plugin.Logger.LogError($"Failed to load {assetName} from {parts[0]} of type { typeof(T).Name }. File not found or invalid");
            return null;
        }
        
        // if no mod is specified check them all
        foreach (var modInfo in Instance.ModInfos.Values)
        {
            T returnV = modInfo.LoadModAsset<T>(assetName);
            if (returnV != null)
            {
                return returnV;
            }
        }

        // check the base resources
        T r = UnityEngine.Resources.Load<T>(assetName);
        if (r != null)
        {
            return r;
        }

        Plugin.Logger.LogError($"Failed to load {assetName} of type {typeof(T).Name}. File not found or invalid");
        return null;
    }


    public void LoadAllJsonAssets()
    {
        foreach(var modInfo in ModInfos.Values)
        foreach (var manager in Instance._managers)
        {
            manager.Value.OnModLoad(modInfo);
        }
    }

    // expose delegate lists
    public static void RegisterPreLibraryInit(string modName, Action action)
    {
        AtlyssToolsLoaderModInfo modInfo = GetModInfo(modName);
        modInfo?.OnPreLibraryInit.Add(action);
    }

    public static void RegisterPostLibraryInit(string modName, Action action)
    {
        AtlyssToolsLoaderModInfo modInfo = GetModInfo(modName);
        modInfo?.OnPostLibraryInit.Add(action);
    }

    public static void RegisterPreCacheInit(string modName, Action action)
    {
        AtlyssToolsLoaderModInfo modInfo = GetModInfo(modName);
        modInfo?.OnPreCacheInit.Add(action);
    }

    public static void RegisterPostCacheInit(string modName, Action action)
    {
        AtlyssToolsLoaderModInfo modInfo = GetModInfo(modName);
        modInfo?.OnPostCacheInit.Add(action);
    }

    public static AtlyssToolsLoaderModInfo GetModInfo(string modName)
    {
        modName = modName.ToLower();
        if (!Instance.ModInfos.ContainsKey(modName))
        {
            Plugin.Logger.LogError($"Mod {modName} not found");
            return null;
        }

        return Instance.ModInfos[modName];
    }

    public static void FindAssetOnly()
    {
        string pluginPath = Paths.PluginPath;
        //find all mods with an AtlyssTools.json at its root
        string[] directories = Directory.GetDirectories(pluginPath);
        
        foreach (var directory in directories)
        {
            string[] files = Directory.GetFiles(directory, "AtlyssTools.json", SearchOption.TopDirectoryOnly);
            if (files.Length == 0)
            {
                continue;
            }
            
            // load the mod data, use the default json loader since JsonUtil is for scriptable objects
            string json = File.ReadAllText(files[0]);
            AtlyssToolsModDef modDef = JsonConvert.DeserializeObject<AtlyssToolsModDef>(json);
            if (modDef == null)
            {
                Plugin.Logger.LogError($"Failed to load AtlyssTools.json for {directory}");
                continue;
            }
            
            Plugin.Logger.LogInfo($"Found AtlyssTools mod {modDef.ModName}");
            
            // for now, don't check the version
            
            
            // check if there is an assembly
            string[] dlls = Directory.GetFiles(directory, "*.dll", SearchOption.TopDirectoryOnly);
            if (dlls.Length == 0)
            {
                LoadPlugin(modDef.ModId, directory, null);
                continue;
            }
            
            // skip, the assembly will be loaded when it registers itself
            continue;
        }
    }
    
    public void GenerateDump()
    {
        // dump all scriptable objects to a json file
        string scriptablesPath = Paths.PluginPath + "/AtlyssToolsDump.json";
        using (StreamWriter writer = new(scriptablesPath))
        {
            foreach (var modInfo in ModInfos.Values)
            {
                // write the mod name
                writer.WriteLine($"Mod: {modInfo.ModId}");
                foreach (var manager in _managers)
                {
                    // write the manager name
                    writer.WriteLine($"Manager: {manager.Key.Name}");
                    foreach (var obj in modInfo.GetModScriptableObjects(manager.Key))
                    {
                        // we just want to write the name
                        writer.WriteLine( manager.Value.GetName(obj as ScriptableObject));
                    }
                }
                
                writer.WriteLine();
                writer.WriteLine();
            }
        }
        
        Plugin.Logger.LogInfo($"Dumped all scriptable objects to {scriptablesPath}");
        
        // dump all assets from asset bundles
        string assetsPath = Paths.PluginPath + "/AtlyssToolsAssetsDump.json";
        
        using (StreamWriter writer = new(assetsPath))
        {
            foreach (var modInfo in ModInfos.Values)
            {
                // write the mod name
                writer.WriteLine($"Mod: {modInfo.ModId}");
                foreach (var bundle in modInfo.Bundles)
                {
                    writer.WriteLine($"Bundle: {bundle.name}");
                    foreach (var asset in bundle.GetAllAssetNames())
                    {
                        writer.WriteLine(asset);
                    }
                }
                
                writer.WriteLine();
                writer.WriteLine();
            }
        }
    }
    
    // list of assets that need to be registered

    private static AtlyssToolsLoader _instance;
    public static AtlyssToolsLoader Instance => _instance ??= new();
}