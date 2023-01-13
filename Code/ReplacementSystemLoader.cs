// Copyright (c) 2023 EchKode
// SPDX-License-Identifier: BSD-3-Clause

using System;
using System.Collections.Generic;

using Entitas;

using HarmonyLib;

using PhantomBrigade;

using UnityEngine;

namespace EchKode.PBMods.ScenarioStateChange
{
	internal static class ReplacementSystemLoader
	{
		public static void Load<T, U>(GameController gameController, string state, Func<Contexts, U> ctor)
			where T : IExecuteSystem
			where U : T
		{
			var gcs = gameController.m_stateDict[state];
			var feature = gcs.m_systems[0];
			var fi = AccessTools.Field(feature.GetType(), "_executeSystems");
			var systems = (List<IExecuteSystem>)fi.GetValue(feature);
			var idx = systems.FindIndex(sys => sys is T);
			if (idx == -1)
			{
				return;
			}

			systems[idx] = ctor(Contexts.sharedInstance);
			Debug.Log($"Mod {ModLink.modIndex} ({ModLink.modId}) extending {typeof(T).Name}");

			// XXX not sure how necessary this is since the profiler is something you generally use from within
			// the Unity editor.
			fi = AccessTools.Field(feature.GetType(), "_executeSystemNames");
			var names = (List<string>)fi.GetValue(feature);
			names[idx] = systems[idx].GetType().FullName;
		}
	}
}
