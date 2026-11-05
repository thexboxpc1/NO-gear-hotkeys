using System;
using BepInEx;
using BepInEx.Configuration;
using Rewired;
using UnityEngine;

namespace GearHotkeys
{
    // ConfigurationManager finds these fields by name via reflection.
    internal sealed class ConfigurationManagerAttributes
    {
        public Action<ConfigEntryBase> CustomDrawer;
        public bool? HideDefaultButton;
        public int? Order;
    }

    [BepInPlugin("com.example.gearhotkeys", "Gear Hotkeys", "2.0.0")]
    public class GearHotkeysPlugin : BaseUnityPlugin
    {
        private sealed class Binding
        {
            public bool IsKeyboard;
            public int KeyId;           // Rewired keyboard element identifier id
            public int Button;          // joystick button index
            public string JoyGuid = ""; // deviceInstanceGuid ("" if unavailable)
            public string JoyName = "";
        }

        private ConfigEntry<string> _retractCfg;
        private ConfigEntry<string> _deployCfg;
        private Binding _retract;
        private Binding _deploy;

        // The entry currently waiting for a key/button press (null = not capturing)
        private ConfigEntry<string> _capturing;

        private void Awake()
        {
            _retractCfg = Bind("Retract Gear", 2);
            _deployCfg = Bind("Deploy Gear", 1);
            _retract = Parse(_retractCfg.Value);
            _deploy = Parse(_deployCfg.Value);

            _retractCfg.SettingChanged += (s, e) => _retract = Parse(_retractCfg.Value);
            _deployCfg.SettingChanged += (s, e) => _deploy = Parse(_deployCfg.Value);

            Logger.LogInfo("Gear Hotkeys loaded.");
        }

        private ConfigEntry<string> Bind(string name, int order)
        {
            return Config.Bind("Controls", name, "",
                new ConfigDescription(
                    "Click the box, then press a keyboard key or any button on any joystick. Esc cancels.",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        CustomDrawer = DrawBinding,
                        HideDefaultButton = true,
                        Order = order
                    }));
        }

        // ---------- ConfigurationManager UI ----------

        private void DrawBinding(ConfigEntryBase entryBase)
        {
            var entry = (ConfigEntry<string>)entryBase;
            bool capturing = ReferenceEquals(_capturing, entry);

            GUILayout.BeginHorizontal();

            string label = capturing
                ? "Press a key or joystick button...  (Esc to cancel)"
                : Describe(Parse(entry.Value));

            if (GUILayout.Button(label, GUILayout.ExpandWidth(true)))
                _capturing = capturing ? null : entry;

            if (GUILayout.Button("Clear", GUILayout.Width(55f)))
            {
                entry.Value = "";
                if (capturing) _capturing = null;
            }

            GUILayout.EndHorizontal();
        }

        // ---------- Update loop ----------

        private void Update()
        {
            // Client-side only: never act on a dedicated server / headless instance
            if (GameManager.IsHeadless || !ReInput.isReady) return;

            if (_capturing != null)
            {
                Capture();
                return; // don't fire gear actions while rebinding
            }

            if (Down(_retract)) SetGear(false);
            if (Down(_deploy)) SetGear(true);
        }

        private void Capture()
        {
            var kb = ReInput.controllers.Keyboard;
            if (kb != null)
            {
                var info = kb.PollForFirstButtonDown();
                if (info.success)
                {
                    if (info.elementIdentifierId == (int)KeyboardKeyCode.Escape)
                    {
                        _capturing = null;
                        return;
                    }
                    _capturing.Value = "K|" + info.elementIdentifierId;
                    _capturing = null;
                    return;
                }
            }

            foreach (var joy in ReInput.controllers.Joysticks)
            {
                var info = joy.PollForFirstButtonDown();
                if (!info.success) continue;

                string guid = joy.deviceInstanceGuid == Guid.Empty ? "" : joy.deviceInstanceGuid.ToString("D");
                _capturing.Value = "J|" + info.elementIndex + "|" + guid + "|" + joy.name;
                _capturing = null;
                return;
            }
        }

        private static bool Down(Binding b)
        {
            if (b == null) return false;

            if (b.IsKeyboard)
            {
                var kb = ReInput.controllers.Keyboard;
                return kb != null && kb.GetButtonDownById(b.KeyId);
            }

            foreach (var joy in ReInput.controllers.Joysticks)
            {
                if (!Matches(b, joy)) continue;
                if (b.Button < joy.buttonCount && joy.GetButtonDown(b.Button)) return true;
            }
            return false;
        }

        // Match by device instance GUID when we have one, otherwise by device name.
        private static bool Matches(Binding b, Joystick joy)
        {
            Guid g;
            if (b.JoyGuid.Length > 0 && Guid.TryParse(b.JoyGuid, out g) && g != Guid.Empty)
                return joy.deviceInstanceGuid == g;
            return joy.name == b.JoyName;
        }

        // ---------- Binding (de)serialisation ----------

        private static Binding Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            try
            {
                var p = s.Split(new[] { '|' }, 4);
                if (p[0] == "K" && p.Length >= 2)
                    return new Binding { IsKeyboard = true, KeyId = int.Parse(p[1]) };
                if (p[0] == "J" && p.Length >= 4)
                    return new Binding { Button = int.Parse(p[1]), JoyGuid = p[2], JoyName = p[3] };
            }
            catch { }
            return null;
        }

        private static string Describe(Binding b)
        {
            if (b == null) return "Not bound  (click, then press a key/button)";
            if (b.IsKeyboard)
            {
                string name = Enum.IsDefined(typeof(KeyboardKeyCode), b.KeyId)
                    ? ((KeyboardKeyCode)b.KeyId).ToString()
                    : "Key " + b.KeyId;
                return "Keyboard: " + name;
            }
            return b.JoyName + " - Button " + (b.Button + 1);
        }

        // ---------- Game action ----------

        private static void SetGear(bool deploy)
        {
            if (GameManager.GetLocalAircraft(out Aircraft aircraft) && aircraft != null)
                aircraft.SetGear(deploy);
        }
    }
}
