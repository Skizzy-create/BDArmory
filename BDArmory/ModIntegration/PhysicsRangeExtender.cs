using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace BDArmory.ModIntegration
{
	public static class PhysicsRangeExtender
	{
		static bool havePRE = false;
		static PropertyInfo modEnabled = null; // bool property
		static PropertyInfo PRERange = null; // int property
		static MethodInfo UpdateRanges = null; // Method for updating ranges after setting PRERange.
		public static bool CheckForPhysicsRangeExtender()
		{
			using var a = AppDomain.CurrentDomain.GetAssemblies().ToList().GetEnumerator();
			while (a.MoveNext())
			{
				if (a.Current.FullName.Split([','])[0] == "PhysicsRangeExtender")
				{
					havePRE = true;
					foreach (var t in a.Current.GetTypes())
					{
						if (t == null) continue;
						if (t.Name == "PreSettings")
						{
							modEnabled = t.GetProperty("ModEnabled", BindingFlags.Public | BindingFlags.Static);
							PRERange = t.GetProperty("GlobalRange", BindingFlags.Public | BindingFlags.Static);
						}
						if (t.Name == "PhysicsRangeExtender")
						{
							UpdateRanges = t.GetMethod("UpdateRanges", BindingFlags.Public | BindingFlags.Static);
						}
					}
					break;
				}
			}
			return havePRE;
		}
		public static bool IsPREEnabled => modEnabled != null && (bool)modEnabled.GetValue(null);
		public static float GetPRERange()
		{
			if (PRERange == null) return 0;
			return (int)PRERange.GetValue(null) * 1000f;
		}
		public static bool SetPRERange(int range)
		{
			if (PRERange == null) return false;
			try
			{
				PRERange.SetValue(null, range / 1000);
				UpdateRanges.Invoke(null, [false]);
			}
			catch (Exception e)
			{
				Debug.LogError($"Failed to update PRE range: {e.Message}");
				return false;
			}
			return true;
		}

		/// <summary>
		/// Work around PhysicsRangeExtender (1.21.0, the latest release) leaking GameEvents callbacks, and with them its destroyed addon instances, on every flight scene:
		/// its OnDestroy adds onVesselSituationChange again instead of removing it (jrodrigv/PhysicsRangeExtender issue #24) and its GUI never removes onShowUI/onHideUI.
		/// The leaked handlers keep running on the dead instances. KSPCommunityFixes only reports these (they belong to a mod's KSPAddon), so remove them here.
		/// Call after the previous scene has been destroyed (e.g., from a Start method).
		/// </summary>
		/// <returns>The number of callbacks removed.</returns>
		public static int RemoveLeakedCallbacks()
		{
			if (!havePRE) return 0;
			int removed = 0;
			try
			{
				removed += RemoveDestroyedPRECallbacks(GameEvents.onVesselSituationChange);
				removed += RemoveDestroyedPRECallbacks(GameEvents.onShowUI);
				removed += RemoveDestroyedPRECallbacks(GameEvents.onHideUI);
			}
			catch (Exception e)
			{
				Debug.LogWarning($"[BDArmory.PhysicsRangeExtender]: Failed to remove leaked PhysicsRangeExtender callbacks: {e.Message}");
			}
			if (removed > 0) Debug.Log($"[BDArmory.PhysicsRangeExtender]: Removed {removed} GameEvents callbacks owned by destroyed PhysicsRangeExtender instances.");
			return removed;
		}

		static int RemoveDestroyedPRECallbacks(BaseGameEvent gameEvent)
		{
			if (gameEvent == null) return 0;
			// EventData<T> and EventVoid both keep their subscribers in a private "events" list of EvtDelegate objects with a public "evt" delegate field.
			var eventsField = gameEvent.GetType().GetField("events", BindingFlags.NonPublic | BindingFlags.Instance);
			if (eventsField == null || eventsField.GetValue(gameEvent) is not System.Collections.IList events) return 0;
			int removed = 0;
			for (int i = events.Count - 1; i >= 0; --i)
			{
				var evtDelegate = events[i];
				if (evtDelegate == null) continue;
				var evtField = evtDelegate.GetType().GetField("evt", BindingFlags.Public | BindingFlags.Instance);
				if (evtField == null || evtField.GetValue(evtDelegate) is not Delegate callback) continue;
				if (callback.Target is UnityEngine.Object owner && owner == null // Unity null check: the owner has been destroyed.
					&& owner.GetType().Assembly.GetName().Name == "PhysicsRangeExtender")
				{
					events.RemoveAt(i);
					++removed;
				}
			}
			return removed;
		}
	}
}