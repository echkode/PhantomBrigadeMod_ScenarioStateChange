// Copyright (c) 2023 EchKode
// SPDX-License-Identifier: BSD-3-Clause

using System.Collections.Generic;

using Entitas;

using HarmonyLib;

using PhantomBrigade;
using PhantomBrigade.Combat.Components;
using PhantomBrigade.Data;
using PBCombatScenarioStateSystem = PhantomBrigade.Combat.Systems.CombatScenarioStateSystem;

using UnityEngine;
using System.Runtime.Remoting.Contexts;

namespace EchKode.PBMods.ScenarioStateChange
{
	public class CombatScenarioStateSystem : PBCombatScenarioStateSystem
	{
		public static void Initialize()
		{
			Heartbeat.Systems.Add(gc =>
				ReplacementSystemLoader.Load<PBCombatScenarioStateSystem, CombatScenarioStateSystem>(
					gc,
					"combat",
					contexts => new CombatScenarioStateSystem(contexts)));
		}

		private readonly PersistentContext persistent;
		private readonly CombatContext combat;
		private readonly IGroup<ActionEntity> actionGroup;
		private readonly IGroup<PersistentEntity> overworldParticipantsGroup;
		private readonly List<string> scopeRemovals = new List<string>();
		private readonly Dictionary<string, ScenarioStateScopeMetadata> scopeCopy;
		private readonly IGroup<CombatEntity> unitsCombatGroup;
		private readonly List<CombatEntity> unitsCombat;

		public CombatScenarioStateSystem(Contexts contexts)
		  : base(contexts)
		{
			var t = Traverse.Create<PBCombatScenarioStateSystem>();
			persistent = contexts.persistent;
			combat = contexts.combat;
			actionGroup = t.Field<IGroup<ActionEntity>>(nameof(actionGroup)).Value;
			overworldParticipantsGroup = t.Field<IGroup<PersistentEntity>>(nameof(overworldParticipantsGroup)).Value;
			scopeRemovals = t.Field<List<string>>(nameof(scopeRemovals)).Value;
			scopeCopy = t.Field<Dictionary<string, ScenarioStateScopeMetadata>>(nameof(scopeCopy)).Value;
			unitsCombatGroup = t.Field<IGroup<CombatEntity>>(nameof(unitsCombatGroup)).Value;
			unitsCombat = t.Field<List<CombatEntity>>(nameof(unitsCombat)).Value;
		}

		protected override void Execute(List<CombatEntity> entities)
		{
			var contexts = (ScenarioStateRefreshContext)combat.scenarioStateRefresh.contexts;
			combat.RemoveScenarioStateRefresh();
			if (!IDUtility.IsGameState("combat"))
			{
				return;
			}

			var currentScenarioAndStep = ScenarioUtility.GetCurrentScenarioAndStep(out var stepCurrent);
			if (currentScenarioAndStep == null || stepCurrent == null)
			{
				Debug.LogWarning("Failed to refresh scenario state: no scenario or no current step found");
				return;
			}

			var states = currentScenarioAndStep.states;
			if (states == null)
			{
				Debug.LogWarningFormat(
					"Failed to refresh scenario state: scenario {0} has no state collection",
					currentScenarioAndStep.key);
				return;
			}

			var stateValues = combat.scenarioStateValues.s;
			var stateScope = combat.scenarioStateScope.s;
			ResetScopeCopy(states, stateScope);

			var hasCombatResolved = persistent.hasCombatResolved;
			var stateValueChanged = false;
			var stateTriggered = false;
			foreach (var kvp in scopeCopy)
			{
				var key = kvp.Key;
				if (!states.ContainsKey(key))
				{
					continue;
				}

				var stateScopeMetadata = kvp.Value;
				var blockScenarioState = states[key];
				if (blockScenarioState == null)
				{
					continue;
				}
				if (!blockScenarioState.evaluated)
				{
					continue;
				}

				var inContext = blockScenarioState.evaluationContext == ScenarioStateRefreshContext.None
					|| (blockScenarioState.evaluationContext & contexts) != ScenarioStateRefreshContext.None;
				if (!inContext)
				{
					continue;
				}

				if (blockScenarioState.evaluationOnOutcome != null)
				{
					var present = blockScenarioState.evaluationOnOutcome.present;
					if (hasCombatResolved != present)
					{
						continue;
					}
				}

				var currentStateValue = stateValues[key];
				var turnCheckPassed = CheckTurn(stateScopeMetadata.entryTurn, blockScenarioState);
				var unitsCheckPassed = CheckUnits(key, blockScenarioState);
				var volumeCheckPassed = CheckVolume(key, blockScenarioState);
				var allChecksPassed = turnCheckPassed && unitsCheckPassed && volumeCheckPassed;
				if (currentStateValue != allChecksPassed)
				{
					stateValues[key] = allChecksPassed;
					stateValueChanged = true;
					Debug.LogWarningFormat(
						"Scenario state {0} has a new value: {1}",
						key,
						allChecksPassed);
				}
				ScenarioUtility.TryTriggeringState(key, out var triggered, out var removeFromScope);
				if (triggered)
				{
					stateTriggered = true;
				}
				if (removeFromScope)
				{
					scopeRemovals.Add(key);
				}
			}

			var removedScopes = RemoveScopes(stateScope);
			if (stateValueChanged || stateTriggered)  // original code: if (stageValueChanged)
			{
				combat.ReplaceScenarioStateValues(stateValues);
				if (stepCurrent.transitionMode == ScenarioTransitionEvaluation.OnStateChange)
				{
					Debug.LogFormat(
						"Step {0} requires transition evaluation on any state change, triggering it...",
						stepCurrent.key);
					combat.isScenarioTransitionRefresh = true;
				}
			}
			CIViewCombatAction.ins.RefreshSelectedUnitActions();
			if (stateValueChanged || removedScopes || stateTriggered)
			{
				CIViewCombatScenarioStatus.ins.Refresh(false);
			}
		}

		private void ResetScopeCopy(
			SortedDictionary<string, DataBlockScenarioState> states,
			Dictionary<string, ScenarioStateScopeMetadata> stateScope)
		{
			scopeRemovals.Clear();
			scopeCopy.Clear();
			var refreshedCombatUnits = false;
			foreach (var kvp in stateScope)
			{
				var key = kvp.Key;
				if (!states.ContainsKey(key))
				{
					continue;
				}

				var blockScenarioState = states[key];
				if (blockScenarioState == null)
				{
					continue;
				}

				if (blockScenarioState.unitChecks != null && !refreshedCombatUnits)
				{
					refreshedCombatUnits = true;
					unitsCombatGroup.GetEntities(unitsCombat);
				}

				scopeCopy.Add(key, kvp.Value);
			}
		}

		private bool CheckTurn(
			int entryTurn,
			DataBlockScenarioState blockScenarioState)
		{
			if (blockScenarioState.turn == null)
			{
				return true;
			}
			var i = combat.currentTurn.i;
			var relativeTurn = i - entryTurn;
			var turnCount = blockScenarioState.turn.relative ? relativeTurn : i;
			return blockScenarioState.turn.IsPassed(true, turnCount);
		}

		private bool CheckUnits(
			string key,
			DataBlockScenarioState blockScenarioState)
		{
			if (blockScenarioState.unitChecks == null)
			{
				return true;
			}

			var stateLocation = ResolveLocation(key, blockScenarioState);
			var counter = 0;
			foreach (var unitCheck in blockScenarioState.unitChecks)
			{
				if (unitCheck == null)
				{
					continue;
				}
				if (unitCheck.locationOccupied != null && stateLocation == null)
				{
					Debug.LogWarningFormat(
						"State {0} can't be evaluated: failed to resolve location data",
						key);
					continue;
				}

				var matches = 0;
				foreach (var combatEntity in unitsCombat)
				{
					var persistentEntity = IDUtility.GetLinkedPersistentEntity(combatEntity);
					if (persistentEntity == null)
					{
						continue;
					}
					if (!ScenarioUtility.IsUnitMatchingCheck(
						persistentEntity,
						combatEntity,
						unitCheck,
						checkCore: true,
						checkActions: true,
						checkLocation: true,
						stateLocation,
						key))
					{
						continue;
					}
					matches += 1;
				}
				var passed = unitCheck.count?.IsPassed(true, matches) ?? matches > 0;
				if (passed)
				{
					counter += 1;
				}
			}
			return counter == blockScenarioState.unitChecks.Count;
		}

		private DataBlockAreaLocation ResolveLocation(
			string key,
			DataBlockScenarioState blockScenarioState)
		{
			if (blockScenarioState.location == null)
			{
				return null;
			}
			if (!combat.hasScenarioStateLocations)
			{
				return null;
			}
			if (!combat.scenarioStateLocations.s.TryGetValue(key, out var locationKey))
			{
				return null;
			}
			return ScenarioUtility.GetLocationFromCurrentArea(locationKey);
		}

		private bool CheckVolume(
			string key,
			DataBlockScenarioState blockScenarioState)
		{
			if (blockScenarioState.volume == null)
			{
				return true;
			}

			var volume = blockScenarioState.volume;
			ScenarioUtility.GetVolumeState(key, out var integrity, out var pointsDestroyed, out var _);
			if (blockScenarioState.volume.visibleInWorld)
			{
				WorldUICombat.OnStateVolumeChange(key);
			}
			var integrityCheck = volume.integrity?.IsPassed(true, integrity) ?? true;
			var volumeDestructionCheck = volume.destructions?.IsPassed(true, pointsDestroyed) ?? true;

			return integrityCheck && volumeDestructionCheck;
		}

		private bool RemoveScopes(Dictionary<string, ScenarioStateScopeMetadata> stateScope)
		{
			var scopeRemoved = false;
			foreach (var scopeRemoval in scopeRemovals)
			{
				if (stateScope.ContainsKey(scopeRemoval))
				{
					scopeRemoved = true;
					stateScope.Remove(scopeRemoval);
				}
			}

			if (scopeRemoved)
			{
				combat.ReplaceScenarioStateScope(stateScope);
			}

			return scopeRemoved;
		}
	}
}
