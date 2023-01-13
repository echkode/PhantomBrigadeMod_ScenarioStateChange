// Copyright (c) 2023 EchKode
// SPDX-License-Identifier: BSD-3-Clause

using System;
using System.Collections.Generic;

using HarmonyLib;

using PhantomBrigade;

namespace EchKode.PBMods.ScenarioStateChange
{
	static class Heartbeat
	{
		public static readonly List<Action<GameController>> Systems = new List<Action<GameController>>();

		public static void Start()
		{
			var fi = AccessTools.DeclaredField(typeof(PhantomBrigade.Heartbeat), "_gameController");
			if (fi == null)
			{
				return;
			}

			var gameController = (GameController)fi.GetValue(null);
			Systems.ForEach(load => load(gameController));
		}
	}
}
