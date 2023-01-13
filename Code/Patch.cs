// Copyright (c) 2023 EchKode
// SPDX-License-Identifier: BSD-3-Clause

using HarmonyLib;

using PBModManager = PhantomBrigade.Mods.ModManager;

using UnityEngine;

namespace EchKode.PBMods.ScenarioStateChange
{
	[HarmonyPatch]
	static class Patch
	{
		[HarmonyPatch(typeof(PBModManager), "ProcessFieldEdit")]
		[HarmonyPrefix]
		static bool Mm_ProcessFieldEditPrefix(
			object target,
			string filename,
			string fieldPath,
			string valueRaw,
			int i,
			string modID,
			string dataTypeName)
		{
			var spec = new ModManager.EditSpec()
			{
				i = i,
				modID = modID,
				filename = ModManager.FindConfigKeyIfEmpty(target, dataTypeName, filename),
				dataTypeName = dataTypeName,
				root = target,
				fieldPath = fieldPath,
				valueRaw = valueRaw,
			};
			Debug.LogWarningFormat(
				"Mod {0} ({1}) applying edit to config {2} path {3}",
				spec.i,
				spec.modID,
				spec.filename,
				spec.fieldPath);
			ModManager.ProcessFieldEdit(spec);
			return false;
		}

		[HarmonyPatch(typeof(PhantomBrigade.Heartbeat), "Start")]
		[HarmonyPrefix]
		static void Hb_StartPrefix()
		{
			Heartbeat.Start();
		}
	}
}
