using System;

using HarmonyLib;

using PBModManager = PhantomBrigade.Mods.ModManager;

using UnityEngine;

namespace EchKode.PBMods.ScenarioStateChange
{
	public class ModLink : PhantomBrigade.Mods.ModLink
	{
		internal static int modIndex;
		internal static string modId;
		internal static string modPath;

		public override void OnLoad(Harmony harmonyInstance)
		{
			// Uncomment to get a file on the desktop showing the IL of the patched methods.
			// Output from FileLog.Log() will trigger the generation of that file regardless if this is set so
			// FileLog.Log() should be put in a guard.
			//Harmony.DEBUG = true;

			modIndex = PBModManager.loadedMods.Count;
			modId = metadata.id;
			modPath = metadata.path;

			var patchAssembly = typeof(ModLink).Assembly;
			Debug.Log($"Mod {metadata.id} is executing OnLoad | Using HarmonyInstance.PatchAll on assembly ({patchAssembly.FullName}) | Directory: {metadata.directory} | Full path: {metadata.path}");
			harmonyInstance.PatchAll(patchAssembly);

			if (Harmony.DEBUG)
			{
				FileLog.Log($"{new string('=', 20)} Start [{DateTime.Now:u}] {new string('=', 20)}");
				FileLog.Log("!!! PBMods patches applied");
			}

			ModManager.Initialize();
			CombatScenarioStateSystem.Initialize();
		}
	}
}
