using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using SpaceCraft;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace ResourceScanner
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class ResourceScannerPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "naagara.planetcrafter.resourcescanner";
        public const string PluginName = "Resource Scanner";
        public const string PluginVersion = "0.1.0";

        const float CacheRefreshSeconds = 5f;
        const float WindowWidth = 620f;
        const float WindowHeight = 620f;

        static ResourceScannerPlugin instance;
        static ConfigEntry<bool> pluginEnabled;
        static ConfigEntry<string> menuBinding;
        static ConfigEntry<float> scanRange;
        static ConfigEntry<float> scanInterval;
        static ConfigEntry<string> selectedResourceId;

        static InputAction menuAction;
        static Harmony harmony;
        static bool showingMenu;
        static bool capturingKey;
        static bool cursorStateSaved;
        static bool previousCursorVisible;
        static CursorLockMode previousCursorLockMode;

        static readonly List<ActionMinable> cachedMinables = new List<ActionMinable>();
        static readonly List<ResourceChoice> resourceChoices = new List<ResourceChoice>();
        static readonly List<ResourceChoice> filteredChoices = new List<ResourceChoice>();
        static ActionMinable currentTarget;
        static float nextCacheRefresh;
        static float nextNearestScan;
        static string searchText = "";
        static Vector2 scrollPosition;
        static FieldInfo currentLanguageField;

        static Texture2D panelTexture;
        static Texture2D headerTexture;
        static Texture2D buttonTexture;
        static Texture2D buttonHoverTexture;
        static Texture2D selectedTexture;
        static Texture2D fieldTexture;
        static Texture2D markerTexture;
        static GUIStyle panelStyle;
        static GUIStyle headerStyle;
        static GUIStyle titleStyle;
        static GUIStyle bodyStyle;
        static GUIStyle mutedStyle;
        static GUIStyle buttonStyle;
        static GUIStyle selectedButtonStyle;
        static GUIStyle textFieldStyle;
        static GUIStyle markerStyle;
        static GUIStyle statusStyle;

        sealed class ResourceChoice
        {
            internal string Id;
            internal string Name;
        }

        void Awake()
        {
            instance = this;
            pluginEnabled = Config.Bind("General", "Enabled", true, "Enable Resource Scanner.");
            menuBinding = Config.Bind("General", "MenuKey", "<Keyboard>/f7", "Key used to open Resource Scanner.");
            scanRange = Config.Bind("Scanner", "Range", 200f, "Maximum resource detection range in metres.");
            scanInterval = Config.Bind("Scanner", "Interval", 0.75f, "Delay in seconds between nearest-resource checks.");
            selectedResourceId = Config.Bind("Scanner", "SelectedResource", "", "Currently tracked resource group id.");

            scanRange.Value = Mathf.Clamp(scanRange.Value, 25f, 1000f);
            scanInterval.Value = Mathf.Clamp(scanInterval.Value, 0.25f, 3f);
            ConfigureMenuAction();

            harmony = new Harmony(PluginGuid);
            harmony.PatchAll(typeof(ResourceScannerPlugin).Assembly);
            SceneManager.sceneLoaded += OnSceneLoaded;
            nextCacheRefresh = 0f;
            nextNearestScan = 0f;
            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            if (menuAction != null)
            {
                menuAction.Disable();
                menuAction.Dispose();
                menuAction = null;
            }
            if (harmony != null)
            {
                harmony.UnpatchSelf();
                harmony = null;
            }
            CloseMenu();
            DestroyTexture(ref panelTexture);
            DestroyTexture(ref headerTexture);
            DestroyTexture(ref buttonTexture);
            DestroyTexture(ref buttonHoverTexture);
            DestroyTexture(ref selectedTexture);
            DestroyTexture(ref fieldTexture);
            DestroyTexture(ref markerTexture);
        }

        static void DestroyTexture(ref Texture2D texture)
        {
            if (texture != null)
            {
                Destroy(texture);
                texture = null;
            }
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            cachedMinables.Clear();
            resourceChoices.Clear();
            currentTarget = null;
            nextCacheRefresh = 0f;
            nextNearestScan = 0f;
            if (showingMenu)
            {
                CloseMenu();
            }
        }

        void Update()
        {
            if (!pluginEnabled.Value)
            {
                if (showingMenu)
                {
                    CloseMenu();
                }
                currentTarget = null;
                return;
            }

            if (capturingKey)
            {
                CaptureMenuKey();
            }
            else if (menuAction != null && menuAction.WasPressedThisFrame())
            {
                if (showingMenu)
                {
                    CloseMenu();
                }
                else
                {
                    OpenMenu();
                }
            }

            if (showingMenu && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                CloseMenu();
            }

            bool shouldScan = showingMenu || !string.IsNullOrEmpty(selectedResourceId.Value);
            if (!shouldScan)
            {
                currentTarget = null;
                return;
            }

            float now = Time.unscaledTime;
            if (now >= nextCacheRefresh)
            {
                RefreshResourceCache();
                nextCacheRefresh = now + CacheRefreshSeconds;
            }
            if (!string.IsNullOrEmpty(selectedResourceId.Value) && now >= nextNearestScan)
            {
                FindNearestTarget();
                nextNearestScan = now + scanInterval.Value;
            }
        }

        static void ConfigureMenuAction()
        {
            if (menuAction != null)
            {
                menuAction.Disable();
                menuAction.Dispose();
            }
            string binding = menuBinding.Value;
            if (string.IsNullOrWhiteSpace(binding))
            {
                binding = "<Keyboard>/f7";
                menuBinding.Value = binding;
            }
            if (binding.IndexOf("<", StringComparison.Ordinal) < 0)
            {
                binding = "<Keyboard>/" + binding;
            }
            try
            {
                menuAction = new InputAction("Resource Scanner", binding: binding);
                menuAction.Enable();
            }
            catch (Exception)
            {
                menuBinding.Value = "<Keyboard>/f7";
                menuAction = new InputAction("Resource Scanner", binding: menuBinding.Value);
                menuAction.Enable();
            }
        }

        static void CaptureMenuKey()
        {
            if (Keyboard.current == null)
            {
                return;
            }
            foreach (var key in Keyboard.current.allKeys)
            {
                if (!key.wasPressedThisFrame)
                {
                    continue;
                }
                if (key == Keyboard.current.escapeKey)
                {
                    capturingKey = false;
                    return;
                }
                menuBinding.Value = "<Keyboard>/" + key.name;
                instance.Config.Save();
                capturingKey = false;
                ConfigureMenuAction();
                return;
            }
        }

        static void OpenMenu()
        {
            showingMenu = true;
            capturingKey = false;
            searchText = "";
            scrollPosition = Vector2.zero;
            SaveAndUnlockCursor();
            RefreshResourceCache();
        }

        static void CloseMenu()
        {
            showingMenu = false;
            capturingKey = false;
            RestoreCursor();
        }

        static void SaveAndUnlockCursor()
        {
            if (!cursorStateSaved)
            {
                previousCursorVisible = Cursor.visible;
                previousCursorLockMode = Cursor.lockState;
                cursorStateSaved = true;
            }
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        static void RestoreCursor()
        {
            if (!cursorStateSaved)
            {
                return;
            }
            Cursor.visible = previousCursorVisible;
            Cursor.lockState = previousCursorLockMode;
            cursorStateSaved = false;
        }

        static void RefreshResourceCache()
        {
            cachedMinables.Clear();
            var found = FindObjectsByType<ActionMinable>(FindObjectsSortMode.None);
            if (found != null)
            {
                cachedMinables.AddRange(found);
            }

            if (showingMenu)
            {
                RebuildResourceChoices();
            }
        }

        static void RebuildResourceChoices()
        {
            var choices = new Dictionary<string, ResourceChoice>(StringComparer.Ordinal);
            foreach (var minable in cachedMinables)
            {
                WorldObject worldObject;
                if (!TryGetWorldObject(minable, out worldObject))
                {
                    continue;
                }
                var group = worldObject.GetGroup();
                string id = group.GetId();
                if (string.IsNullOrEmpty(id) || choices.ContainsKey(id))
                {
                    continue;
                }
                choices[id] = new ResourceChoice
                {
                    Id = id,
                    Name = SafeGroupName(group)
                };
            }
            resourceChoices.Clear();
            resourceChoices.AddRange(choices.Values);
            resourceChoices.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase));
        }

        static bool TryGetWorldObject(ActionMinable minable, out WorldObject worldObject)
        {
            worldObject = null;
            if (minable == null || minable.gameObject == null || !minable.gameObject.activeInHierarchy)
            {
                return false;
            }
            var associated = minable.GetComponent<WorldObjectAssociated>();
            if (associated == null)
            {
                associated = minable.GetComponentInParent<WorldObjectAssociated>();
            }
            if (associated == null)
            {
                return false;
            }
            worldObject = associated.GetWorldObject();
            return worldObject != null && worldObject.GetGroup() != null;
        }

        static string SafeGroupName(Group group)
        {
            if (group == null)
            {
                return T("unknown");
            }
            try
            {
                string value = Readable.GetGroupName(group);
                return string.IsNullOrEmpty(value) ? group.GetId() : value;
            }
            catch
            {
                return group.GetId();
            }
        }

        static void FindNearestTarget()
        {
            var players = Managers.GetManager<PlayersManager>();
            var player = players == null ? null : players.GetActivePlayerController();
            if (player == null)
            {
                currentTarget = null;
                return;
            }

            Vector3 playerPosition = player.transform.position;
            float maxDistanceSquared = scanRange.Value * scanRange.Value;
            float nearestDistanceSquared = float.MaxValue;
            ActionMinable nearest = null;

            foreach (var minable in cachedMinables)
            {
                WorldObject worldObject;
                if (!TryGetWorldObject(minable, out worldObject))
                {
                    continue;
                }
                if (!string.Equals(worldObject.GetGroup().GetId(), selectedResourceId.Value, StringComparison.Ordinal))
                {
                    continue;
                }
                float distanceSquared = (minable.transform.position - playerPosition).sqrMagnitude;
                if (distanceSquared <= maxDistanceSquared && distanceSquared < nearestDistanceSquared)
                {
                    nearestDistanceSquared = distanceSquared;
                    nearest = minable;
                }
            }
            currentTarget = nearest;
        }

        void OnGUI()
        {
            if (!pluginEnabled.Value)
            {
                return;
            }
            EnsureStyles();
            DrawTrackingIndicator();
            if (!showingMenu)
            {
                return;
            }

            SaveAndUnlockCursor();
            float width = Mathf.Min(WindowWidth, Screen.width - 30f);
            float height = Mathf.Min(WindowHeight, Screen.height - 30f);
            Rect windowRect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            GUI.ModalWindow(971307, windowRect, DrawScannerWindow, "", GUIStyle.none);
        }

        static void DrawScannerWindow(int id)
        {
            float width = Mathf.Min(WindowWidth, Screen.width - 30f);
            float height = Mathf.Min(WindowHeight, Screen.height - 30f);
            GUI.Box(new Rect(0f, 0f, width, height), GUIContent.none, panelStyle);
            GUI.Box(new Rect(0f, 0f, width, 64f), GUIContent.none, headerStyle);

            GUI.Label(new Rect(24f, 12f, width - 48f, 30f), T("title"), titleStyle);
            GUI.Label(new Rect(24f, 40f, width - 48f, 20f), T("subtitle"), mutedStyle);

            float radius = GUI.HorizontalSlider(new Rect(130f, 86f, width - 310f, 22f), scanRange.Value, 25f, 1000f);
            radius = Mathf.Round(radius / 5f) * 5f;
            if (!Mathf.Approximately(radius, scanRange.Value))
            {
                scanRange.Value = radius;
                nextNearestScan = 0f;
            }
            GUI.Label(new Rect(24f, 80f, 105f, 28f), T("range"), bodyStyle);
            GUI.Label(new Rect(width - 165f, 80f, 140f, 28f), ((int)scanRange.Value) + " m", bodyStyle);

            GUI.Label(new Rect(24f, 119f, 105f, 28f), T("shortcut"), bodyStyle);
            if (capturingKey)
            {
                GUI.Label(new Rect(130f, 119f, 220f, 28f), T("pressKey"), mutedStyle);
            }
            else
            {
                GUI.Label(new Rect(130f, 119f, 140f, 28f), GetMenuKeyLabel(), bodyStyle);
                if (GUI.Button(new Rect(275f, 116f, 120f, 31f), T("change"), buttonStyle))
                {
                    capturingKey = true;
                }
            }

            bool isTracking = !string.IsNullOrEmpty(selectedResourceId.Value);
            string selectedName = GetSelectedResourceName();
            GUI.Label(new Rect(24f, 158f, width - 180f, 28f), isTracking ? string.Format(T("tracking"), selectedName) : T("notTracking"), bodyStyle);
            if (isTracking && GUI.Button(new Rect(width - 165f, 154f, 140f, 32f), T("stop"), buttonStyle))
            {
                selectedResourceId.Value = "";
                currentTarget = null;
                instance.Config.Save();
            }

            GUI.Label(new Rect(24f, 204f, 90f, 28f), T("search"), bodyStyle);
            GUI.SetNextControlName("ResourceScannerSearch");
            searchText = GUI.TextField(new Rect(115f, 201f, width - 140f, 32f), searchText, textFieldStyle);

            Rect listRect = new Rect(24f, 248f, width - 48f, height - 316f);
            var displayed = GetDisplayedChoices();
            float contentHeight = Mathf.Max(listRect.height - 4f, displayed.Count * 40f + 8f);
            scrollPosition = GUI.BeginScrollView(listRect, scrollPosition, new Rect(0f, 0f, listRect.width - 18f, contentHeight));
            float y = 4f;
            foreach (var choice in displayed)
            {
                bool selected = string.Equals(choice.Id, selectedResourceId.Value, StringComparison.Ordinal);
                string label = selected ? "●  " + choice.Name : "○  " + choice.Name;
                if (GUI.Button(new Rect(0f, y, listRect.width - 24f, 34f), label, selected ? selectedButtonStyle : buttonStyle))
                {
                    selectedResourceId.Value = choice.Id;
                    instance.Config.Save();
                    nextNearestScan = 0f;
                    FindNearestTarget();
                    CloseMenu();
                }
                y += 40f;
            }
            GUI.EndScrollView();

            if (displayed.Count == 0)
            {
                GUI.Label(new Rect(40f, 270f, width - 80f, 60f), T("noResources"), mutedStyle);
            }
            if (GUI.Button(new Rect(width - 165f, height - 52f, 140f, 34f), T("close"), buttonStyle))
            {
                instance.Config.Save();
                CloseMenu();
            }
        }

        static IList<ResourceChoice> GetDisplayedChoices()
        {
            if (string.IsNullOrWhiteSpace(searchText))
            {
                return resourceChoices;
            }
            filteredChoices.Clear();
            foreach (var choice in resourceChoices)
            {
                if (choice.Name.IndexOf(searchText, StringComparison.CurrentCultureIgnoreCase) >= 0
                    || choice.Id.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    filteredChoices.Add(choice);
                }
            }
            return filteredChoices;
        }

        static string GetSelectedResourceName()
        {
            if (string.IsNullOrEmpty(selectedResourceId.Value))
            {
                return T("unknown");
            }
            foreach (var choice in resourceChoices)
            {
                if (string.Equals(choice.Id, selectedResourceId.Value, StringComparison.Ordinal))
                {
                    return choice.Name;
                }
            }
            if (currentTarget != null)
            {
                WorldObject worldObject;
                if (TryGetWorldObject(currentTarget, out worldObject))
                {
                    return SafeGroupName(worldObject.GetGroup());
                }
            }
            return selectedResourceId.Value;
        }

        static void DrawTrackingIndicator()
        {
            if (string.IsNullOrEmpty(selectedResourceId.Value) || showingMenu)
            {
                return;
            }
            string resourceName = GetSelectedResourceName();
            var players = Managers.GetManager<PlayersManager>();
            var player = players == null ? null : players.GetActivePlayerController();
            if (player == null)
            {
                return;
            }

            if (currentTarget == null)
            {
                string noSignal = string.Format(T("noSignal"), resourceName, (int)scanRange.Value, GetMenuKeyLabel());
                GUI.Label(new Rect((Screen.width - 440f) * 0.5f, 24f, 440f, 38f), noSignal, statusStyle);
                return;
            }

            float distance = Vector3.Distance(player.transform.position, currentTarget.transform.position);
            string label = resourceName + "  •  " + Mathf.RoundToInt(distance) + " m";
            Camera camera = Camera.main;
            if (camera == null)
            {
                GUI.Label(new Rect((Screen.width - 320f) * 0.5f, 24f, 320f, 38f), label, statusStyle);
                return;
            }

            Vector3 screen = camera.WorldToScreenPoint(currentTarget.transform.position + Vector3.up * 0.35f);
            float x = screen.x;
            float y = Screen.height - screen.y;
            bool behind = screen.z <= 0f;
            if (behind)
            {
                x = Screen.width - x;
                y = Screen.height - y;
            }

            const float halfWidth = 105f;
            const float halfHeight = 22f;
            bool onScreen = !behind && x >= halfWidth && x <= Screen.width - halfWidth && y >= 70f && y <= Screen.height - halfHeight;
            if (onScreen)
            {
                GUI.Label(new Rect(x - halfWidth, y - halfHeight, halfWidth * 2f, halfHeight * 2f), "◆  " + label, markerStyle);
                return;
            }

            float clampedX = Mathf.Clamp(x, halfWidth + 12f, Screen.width - halfWidth - 12f);
            float clampedY = Mathf.Clamp(y, 78f, Screen.height - halfHeight - 16f);
            Vector2 direction = new Vector2(x - Screen.width * 0.5f, y - Screen.height * 0.5f);
            string arrow;
            if (Mathf.Abs(direction.x) > Mathf.Abs(direction.y))
            {
                arrow = direction.x < 0f ? "<" : ">";
            }
            else
            {
                arrow = direction.y < 0f ? "^" : "v";
            }
            GUI.Label(new Rect(clampedX - halfWidth, clampedY - halfHeight, halfWidth * 2f, halfHeight * 2f), arrow + "  " + label, markerStyle);
        }

        static string GetMenuKeyLabel()
        {
            string value = menuBinding.Value ?? "<Keyboard>/f7";
            int slash = value.LastIndexOf('/');
            if (slash >= 0 && slash + 1 < value.Length)
            {
                value = value.Substring(slash + 1);
            }
            if (value.StartsWith("digit", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(5);
            }
            return value.ToUpperInvariant();
        }

        static bool IsEnglish()
        {
            try
            {
                if (currentLanguageField == null)
                {
                    currentLanguageField = AccessTools.Field(typeof(Localization), "currentLangage");
                }
                string language = currentLanguageField == null ? null : currentLanguageField.GetValue(null) as string;
                return !string.IsNullOrEmpty(language)
                    && (language.StartsWith("en", StringComparison.OrdinalIgnoreCase)
                        || language.IndexOf("english", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            catch
            {
                return false;
            }
        }

        static string T(string key)
        {
            bool english = IsEnglish();
            switch (key)
            {
                case "title": return "RESOURCE SCANNER";
                case "subtitle": return english ? "Select one resource to track. Only the nearest deposit is displayed." : "Choisis une ressource. Seul le gisement le plus proche sera affiche.";
                case "range": return english ? "Range" : "Rayon";
                case "shortcut": return english ? "Shortcut" : "Raccourci";
                case "pressKey": return english ? "Press a keyboard key (Esc to cancel)" : "Appuie sur une touche (Echap pour annuler)";
                case "change": return english ? "Change" : "Modifier";
                case "tracking": return english ? "Tracking: {0}" : "Suivi : {0}";
                case "notTracking": return english ? "No resource selected" : "Aucune ressource selectionnee";
                case "stop": return english ? "Stop tracking" : "Arreter le suivi";
                case "search": return english ? "Search" : "Chercher";
                case "noResources": return english ? "No mineable resource is currently loaded nearby." : "Aucune ressource minable n'est actuellement chargee a proximite.";
                case "close": return english ? "Close" : "Fermer";
                case "noSignal": return english ? "{0}: no deposit within {1} m  •  {2} to change" : "{0} : aucun gisement a moins de {1} m  •  {2} pour changer";
                case "unknown": return english ? "Unknown" : "Inconnu";
                default: return key;
            }
        }

        static Texture2D MakeTexture(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            texture.hideFlags = HideFlags.HideAndDontSave;
            return texture;
        }

        static void EnsureStyles()
        {
            if (panelStyle != null)
            {
                return;
            }
            panelTexture = MakeTexture(new Color(0.035f, 0.065f, 0.105f, 0.98f));
            headerTexture = MakeTexture(new Color(0.055f, 0.17f, 0.25f, 1f));
            buttonTexture = MakeTexture(new Color(0.09f, 0.16f, 0.22f, 1f));
            buttonHoverTexture = MakeTexture(new Color(0.10f, 0.34f, 0.44f, 1f));
            selectedTexture = MakeTexture(new Color(0.08f, 0.48f, 0.58f, 1f));
            fieldTexture = MakeTexture(new Color(0.015f, 0.035f, 0.055f, 1f));
            markerTexture = MakeTexture(new Color(0.02f, 0.08f, 0.12f, 0.92f));

            panelStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = panelTexture },
                border = new RectOffset(2, 2, 2, 2)
            };
            headerStyle = new GUIStyle(panelStyle)
            {
                normal = { background = headerTexture }
            };
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 23,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.78f, 0.94f, 1f, 1f) }
            };
            bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Color.white }
            };
            mutedStyle = new GUIStyle(bodyStyle)
            {
                fontSize = 14,
                wordWrap = true,
                normal = { textColor = new Color(0.70f, 0.79f, 0.84f, 1f) }
            };
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 15,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(14, 12, 5, 5),
                normal = { background = buttonTexture, textColor = Color.white },
                hover = { background = buttonHoverTexture, textColor = Color.white },
                active = { background = selectedTexture, textColor = Color.white }
            };
            selectedButtonStyle = new GUIStyle(buttonStyle)
            {
                fontStyle = FontStyle.Bold,
                normal = { background = selectedTexture, textColor = Color.white }
            };
            textFieldStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 16,
                padding = new RectOffset(10, 10, 6, 6),
                normal = { background = fieldTexture, textColor = Color.white },
                focused = { background = fieldTexture, textColor = Color.white }
            };
            markerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { background = markerTexture, textColor = new Color(0.48f, 0.94f, 1f, 1f) }
            };
            statusStyle = new GUIStyle(markerStyle)
            {
                fontSize = 14,
                wordWrap = false
            };
        }

        [HarmonyPatch]
        static class BlockPlayerInputWhileMenuOpen
        {
            static IEnumerable<MethodBase> TargetMethods()
            {
                string[] names =
                {
                    "OnActionDispatcher",
                    "OnAutoForwardDispatcher",
                    "OnBlockViewDispatcher",
                    "OnCancelActionDispatcher",
                    "OnCloseUiDispatcher",
                    "OnEscapeDispatcher",
                    "OnMoveDispatcher",
                    "OnOpenConstructionDispatcher",
                    "OnOpenInventoryDispatcher",
                    "OnPhotoModeDispatcher",
                    "OnPressEnterDispatcher",
                    "OnRotateObjectDispatcher",
                    "OnShowMapDispatcher",
                    "OnToggleLightDispatcher",
                    "OnUnstuckDispatcher"
                };
                foreach (string name in names)
                {
                    MethodInfo method = AccessTools.Method(typeof(PlayerInputDispatcher), name);
                    if (method != null)
                    {
                        yield return method;
                    }
                }
            }

            static bool Prefix()
            {
                return !showingMenu;
            }
        }
    }
}
