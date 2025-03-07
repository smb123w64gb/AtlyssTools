// harmony patch


using AtlyssTools.Utility;
using HarmonyLib;

namespace AtlyssTools.Patches;

[HarmonyPatch(typeof(GameManager), "Cache_ScriptableAssets")]
public class GameManagerPatches
{
    [HarmonyPrefix]
    private static void Prefix(GameManager __instance)
    {
        // write a test message to the console
        Plugin.Logger.LogInfo("GameManager.Cache_ScriptableAssets prefix patch");
        // add our custom skills to the cache
        // do a dump of the skills
        AtlyssToolsLoader.Instance.State = LoaderStateMachine.LoadState.PreCacheInit; // run the pre cache init
    }

    [HarmonyPostfix]
    private static void Postfix(GameManager __instance)
    {
        // write a test message to the console
        Plugin.Logger.LogInfo("GameManager.Cache_ScriptableAssets postfix patch");
        // add our custom skills to the cache
        // do a dump of the skills
        AtlyssToolsLoader.Instance.State = LoaderStateMachine.LoadState.PostCacheInit; // run the post cache init
    }
}

[HarmonyPatch(typeof(PlayerCasting), "Init_SkillLibrary")]
public class CastingPatches
{
    [HarmonyPrefix]
    private static void Prefix(PlayerCasting __instance)
    {
        AtlyssToolsLoader.Instance.State = LoaderStateMachine.LoadState.PreLibraryInit; // run the post library init
    }

    [HarmonyPostfix]
    private static void Postfix(PlayerCasting __instance)
    {
        AtlyssToolsLoader.Instance.State =
            LoaderStateMachine.LoadState.PostLibraryInit; // run the post library init
    }
}