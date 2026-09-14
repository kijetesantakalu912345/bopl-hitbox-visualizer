// Copyright (c) 2024/2025/2026 Jo912345/kijetesantakalu. released under the MIT license (see LICENSE.txt file).

using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using BoplFixedMath;
using static HitBoxVisualizerPlugin.hitboxVisualizerLineStyling;
using BepInEx.Configuration;
using System.Xml.Serialization;
//using TMPro;
using UnityEngine.UIElements.Collections;
using BepInEx.Logging;
using UnityEngine.SceneManagement;

namespace HitBoxVisualizerPlugin
{

    [BepInPlugin("com.jo912345.hitboxVisualizePlugin", "HitBoxVisualizer", PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        // int is the instance ID
        public static Dictionary<int, DPhysicsBox> DPhysBoxDict = [];
        public static Dictionary<int, DPhysicsCircle> DPhysCircleDict = [];

        //public static Dictionary<int, Circle> CirlceDict = [];

        public static HitboxLineGroup DebugLineGroup;

        public static List<HitboxLineGroup> ManualLineGroups = [];

        public static ListOfLineHolderGameObjs poolOfLineHolderGameObjs = new ListOfLineHolderGameObjs();

        public ConfigEntry<float> CONFIG_drawingThickness;
        public ConfigEntry<float> CONFIG_debugLineLifetime;

        public ConfigEntry<string> CONFIG_rectColors;
        public ConfigEntry<string> CONFIG_circleColors;
        public ConfigEntry<string> CONFIG_disabledColors;

        public ConfigEntry<bool> CONFIG_updateToNewDefaults;
        public ConfigEntry<string> CONFIG_lastConfigVersion;

        public static float drawingThickness = 0.5f;
        public static float debugLineLifetime = 1.5f;
        public static int circleDrawingMinAmountOfLines = 14;

        public new static ManualLogSource Logger;

        private readonly Harmony harmony = new Harmony("com.jo912345.hitboxVisualizePlugin");

        public void Awake()
        {
            Logger = base.Logger;
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");

            LineDrawing.setUpLineRendererMaterialToDefault();
            poolOfLineHolderGameObjs.SetAllLineRendererMaterials(LineDrawing.lineRendererBaseMaterial);
            DebugLineGroup = new HitboxLineGroup([], lineDrawingStyle.debugDefault);

            SceneManager.activeSceneChanged += OnSceneChange;
            loadConfigValues();
            harmony.PatchAll();
            Logger.LogInfo("all `DPhysicsBox`es/`DPhysicsCircle`s should now have their hitboxes drawn!");
        }

        public void loadConfigValues()
        {
            // drawing settings
            CONFIG_drawingThickness = Config.Bind(
                "Drawing Settings",
                "drawingThickness",
                0.2f,
                "How thick should hitbox lines be drawn? Note that this value is in unity world units and not pixels. For reference, 0.2 is relatively thin, and 0.5 is large.\n" +
                "Rectangular hitboxes are now always drawn accurately, never extending past the real hitbox edge (excluding small artifacts caused by a lack of anti-aliasing)."
            );
            CONFIG_debugLineLifetime = Config.Bind(
                "Drawing Settings",
                "debugLineLifetime",
                1.5f,
                "How many seconds should debug lines (ie non hitbox stuff like raycasts) live before being destroyed? This is purely visual."
            );


            // line color settings
            CONFIG_rectColors = Config.Bind(
                "Line Color Settings",
                "rectangleColors",
                "#FF0000CC,#FFEB04CC,#00FF00CC,#0000FFCC",
                "What colors should rectangles use? Colors are separated by commas. Any colors after the 4th option will be ignored.\n" +
                "You can use #RGB, #RRGGBB, #RGBA, or #RRGGBBAA formatting, or one of the color words specified here:\n" +
                "https://docs.unity3d.com/2022.3/Documentation/ScriptReference/ColorUtility.TryParseHtmlString.html");
            CONFIG_circleColors = Config.Bind(
                "Line Color Settings",
                "circleColors",
                "#FF0000CC,#FFEB04CC,#00FF00CC,#0000FFCC,#FF00FFCC",
                "What colors should circles use? Colors are separated by commas. You may only use up to 7 colors per the limitations of Unity's `Gradient` class.\n" +
                "The first color is also used as the last color to loop the gradient.\n" +
                "You can use #RGB, #RRGGBB, #RGBA, or #RRGGBBAA formatting, or one of the color words specified here:\n" +
                "https://docs.unity3d.com/2022.3/Documentation/ScriptReference/ColorUtility.TryParseHtmlString.html");
            CONFIG_disabledColors = Config.Bind(
                "Line Color Settings",
                "disabledColors",
                "#333333FF,#CCCCCCFF,#333333FF,#CCCCCCFF",
                "What colors should disabled objects use? This includes rectanges and circles. Colors are separated by commas.\n" +
                "You may only use up to 7 colors (the gradient is looped), per the limitations of Unity's `Gradient` class.\n" +
                "You can use #RGB, #RRGGBB, #RGBA, or #RRGGBBAA formatting, or one of the color words specified here:\n" +
                "https://docs.unity3d.com/2022.3/Documentation/ScriptReference/ColorUtility.TryParseHtmlString.html");

            var importantToOverrideConfigDefaults = new Dictionary<String, Dictionary<ConfigDefinition, object>>(){
                { "3.0.0",  new Dictionary<ConfigDefinition, object>(){ {new ConfigDefinition("Line Color Settings", "disabledColors"), "#000000CC,#FFFFFFCC,#000000CC,#FFFFFFCC"} } }
            };

            CONFIG_updateToNewDefaults = Config.Bind(
                "Default Reset Settings (ignore this section unless you know what you are doing)",
                "AllowUpdateToNewDefaults",
                true,
                "Should the mod update settings left in a default state to a new default if an update changes the default for that setting?\n" +
                "This will *NEVER* overwrite settings that were changed by the user.");

            // 3.1.0 is the version that this setting was added, and 3.0.0 was the previous version.
            CONFIG_lastConfigVersion = Config.Bind(
                "Default Reset Settings (ignore this section unless you know what you are doing)",
                "lastConfigVersion",
                "3.0.0",
                "What's the latest version of the mod that ran the default setting replacement logic? This value shouldn't be changed manually.");

            // this whole system is quite overengineered (and kinda hacky) but the original `disabledPhys` colors didn't contrast very well in some places (mainly space maps) and
            // I'm pretty sure most people don't mess with the config file. So to improve the experience for the average user, I've added this.
            // this will only overwrite settings that were left in their default state and where updating to a new default is important enough to justify using this feature.

            if (CONFIG_updateToNewDefaults.Value && versionString_IsGreater(PluginInfo.PLUGIN_VERSION, CONFIG_lastConfigVersion.Value) &&
                importantToOverrideConfigDefaults.ContainsKey(CONFIG_lastConfigVersion.Value))
            {
                Logger.LogInfo("Update detected! Old config values left in their default state may be updated. This behaviour can be disabled by setting `AllowUpdateToNewDefaults`"
                    + " to false.");
                Dictionary<ConfigDefinition, object> oldDefaults;
                var keyExists = importantToOverrideConfigDefaults.TryGetValue(CONFIG_lastConfigVersion.Value, out oldDefaults);
                if (!keyExists)
                {
                    throw new InvalidOperationException("importantToOverrideConfigDefaults.TryGetValue() failed somehow despite literally just checking that importantToOverrideConfigDefaults.ContainsKey(CONFIG_lastConfigVersion.Value) is true");
                }
                var oldKeys = oldDefaults.Keys.ToArray();
                for (int i = 0; i < oldDefaults.Keys.Count; i++)
                {
                    if (Config.ContainsKey(oldKeys[i]) && oldDefaults.Get(oldKeys[i]).Equals(Config.Get(oldKeys[i]).BoxedValue))
                    {
                        Logger.LogInfo("updated old, left-as-default setting `" + oldKeys[i].ToString() + "` from `" + Config.Get(oldKeys[i]).BoxedValue + "` to `"
                            + Config.Get(oldKeys[i]).DefaultValue + "`!");
                        Config.Get(oldKeys[i]).BoxedValue = Config.Get(oldKeys[i]).DefaultValue;
                    }
                }
                CONFIG_lastConfigVersion.Value = PluginInfo.PLUGIN_VERSION;
            }

            // ADD A SETTING FOR SHOWING INACTIVE HITBOXES! not to be confused with disabled hitboxes.

            drawingStyleToLineColors[lineDrawingStyle.defaultColors] = loadConfigColorsFromString(CONFIG_rectColors, drawingStyleToLineColors[lineDrawingStyle.defaultColors], false);
            drawingStyleToLineColors[lineDrawingStyle.circleColors] = loadConfigColorsFromString(CONFIG_circleColors, drawingStyleToLineColors[lineDrawingStyle.circleColors]);
            drawingStyleToLineColors[lineDrawingStyle.disabledPhys] = loadConfigColorsFromString(CONFIG_disabledColors, drawingStyleToLineColors[lineDrawingStyle.disabledPhys]);

            drawingThickness = CONFIG_drawingThickness.Value;
            debugLineLifetime = CONFIG_debugLineLifetime.Value;
        }

        public bool versionString_IsGreater(string v1, string v2)
        {
            if (v1 == v2)
            {
                return false;
            }
            int[] v1Digits = Array.ConvertAll(v1.Split('.'), int.Parse);
            int[] v2Digits = Array.ConvertAll(v2.Split('.'), int.Parse);
            if (v1Digits.Length != v2Digits.Length)
            {
                throw new NotImplementedException("v1Digits.Length != v2Digits.Length. Comparing version strings of different lengths is unimplemented.");
            }
            int i = 0;
            for (i = 0; i < v1Digits.Length; i++)
            {
                if (v1Digits[i] > v2Digits[i])
                {
                    return true;
                }
            }
            return false;
        }

        public List<Color> loadConfigColorsFromString(ConfigEntry<String> selectedConfigEntry, List<Color> normalColors, bool gradientColorLimit = true)
        {
            String listString = selectedConfigEntry.Value;
            String categoryName = selectedConfigEntry.Definition.Key;
            String[] colorStrings = listString.Split(',');
            List<Color> colorList = new List<Color>();
            Color curColor = new Color();
            foreach (String colorString in colorStrings)
            {
                bool success = ColorUtility.TryParseHtmlString(colorString, out curColor);
                if (!success)
                {
                    Logger.LogError("color string \""+ colorString + "\" is invalid, falling back to default colors for " + categoryName + " instead. " +
                        "Check the config file or https://docs.unity3d.com/2022.3/Documentation/ScriptReference/ColorUtility.TryParseHtmlString.html for formatting options.");
                    return normalColors;
                }
                colorList.Add(curColor);
            }
            if (gradientColorLimit && colorList.Count() > 7)
            {
                Logger.LogError("color string \"" + listString + "\" is invalid, falling back to default colors for " + categoryName + " instead. " +
                    "Using more than 7 colors (the gradient is looped) in a gradient is not supported, due to a unity limitation.");
                return normalColors;
            }
            return colorList;
        }

        public void Start()
        {
            // increase the loop count to get more free ManualLineGroups indicies.
            // this isn't the most scalable possible solution because index collisions need to be avoided but that's a fine enough tradeoff for what I'm doing.
            // this is still very scalable.
            for (int i = 0; i < 10; i++)
            {
                ManualLineGroups.Add(new HitboxLineGroup([]));
            }
            // poolOfLineHolderGameObjs = new ListOfLineHolderGameObjs(); is redundant here, required after bopl 2.4.3+ for a non-zero minCapacity.
            // extra game objects are cleaned up at boot by unity in newer versions.
            poolOfLineHolderGameObjs = new ListOfLineHolderGameObjs();
        }

        public void OnDestroy()
        {
            SceneManager.activeSceneChanged -= OnSceneChange;
            harmony.UnpatchSelf();
            Logger.LogError("hitboxVisualizer has been unloaded. (if you see this when starting the game, it's likely that `HideManagerGameObject = false` in `BepInEx.cfg`. please enable it!)");
        }

        // (later) I probably won't draw lines for more functions at this point unless I'm trying to debug something specific.
        // TODO: add raycasts
        // there's raycasting/interesting stuff in:
        // * DetPhysics (mostly calls from Raycast?)
        // * Raycast
        // * Tools
        // * everything that uses OnDrawGizmos()/related functions
        //   * could I call these manually? (some of these call Debug.DrawLine())
        //   * 
        // - GameSessionHandler.DrawCross(), kinda

        // RE-ENABLING THIS WILL BREAK DELTA TIMES UNLESS IT'S SPECIFICALLY ACCOUNTED FOR!
        // at least if they're added when you're reading this
        /*public void Update()
        {
            updateHitboxes();
        }*/

        // <strikethrough>the lineRenderer lags behind if LateUpdate isn't used</strikethrough>
        // nevermind, i guess not anymore? (currently writing during 2.5.0).
        public void LateUpdate()
        {
            // updateHitboxes(Time.unscaledDeltaTime);
            // actually no I want raycasts to stay visible if I'm lowering/pausing the game speed.
            updateHitboxes(Time.deltaTime);
        }

        public void OnSceneChange(Scene current, Scene next)
        {
            DebugLineGroup.groupLines.Clear();
            for (int i = 0; i < ManualLineGroups.Count; i++)
            {
                ManualLineGroups[i].groupLines.Clear();
            }
        }

        public void updateHitboxes(float deltaSeconds)
        {
            Tuple<List<HitboxLineGroup>, List<HitboxLineGroup>> ListOflineGroupTuple = calculateHitBoxShapeComponentLines(DPhysBoxDict, DPhysCircleDict);

            // rectangles + DebugLines + ManualLineGroups
            List<HitboxLineGroup> HitboxComponentLines_NoDistortion = ListOflineGroupTuple.Item1;
            HitboxComponentLines_NoDistortion.AddRange(ManualLineGroups);
            HitboxComponentLines_NoDistortion.Add(DebugLineGroup);

            // circles
            // circles already have very little distortion (likely due to their much shallower turns at each point)
            // and would also cost a ton of extra line holder game objects to render with 1 game object per line.
            List<HitboxLineGroup> HitboxComponentLines = ListOflineGroupTuple.Item2;
            
            LineDrawing.drawLinesAsLineRendererPositions(HitboxComponentLines);
            LineDrawing.drawLinesIndividuallyWithHolderGameObjects(HitboxComponentLines_NoDistortion);

            DebugLineGroup.TickLineLifetimes(deltaSeconds);
            // I should probably add a flag to HitboxLineGroup for if the grouped lines can decay...
            // then either remove or keep the flag on the individal lines themselves.
            // looping through thousands of lines, even just quickly checking a flag before going to the next item,
            // is wasteful when there are large groups that will never contain decaying lines.
            // for now I'll just comment this out because at time of writing nothing in `ManualLineGroups` contians decaying lines.
            /*for (int i = 0; i < ManualLineGroups.Count; i++)
            {
                ManualLineGroups[i].TickLineLifetimes(deltaSeconds);
            }*/
        }

        public Tuple<List<HitboxLineGroup>, List<HitboxLineGroup>> calculateHitBoxShapeComponentLines(Dictionary<int, DPhysicsBox> inputDPhysBoxDict, Dictionary<int, DPhysicsCircle> inputDPhysCircleDict)
        {
            // rects
            var newHitboxLineGroups_NoDistortion = new List<HitboxLineGroup> ();
            // circles
            var newHitboxLineGroups_DistortionAllowed = new List<HitboxLineGroup>();

            // CALCULATE RECTS
            for (int i = 0; i < inputDPhysBoxDict.Values.ToList().Count; i++)
            {
                var currBox = inputDPhysBoxDict.Values.ToList()[i];

                if (currBox == null)
                {
                    Logger.LogWarning("inputDPhysBoxDict.Values.ToList()[i] == null, removing from array. a round probably ended.");
                    inputDPhysBoxDict.Remove(inputDPhysBoxDict.Keys.ToList()[i]);
                    continue;
                }
                if (!currBox.gameObject.activeSelf)
                {
                    continue;
                }
                
                var boxOfCurrBox = currBox.Box();
                var boxScale = currBox.Scale;


                // via just staring into the decompiled code for a while, I realized I should try accessing the values from the physics engine.
                // accessing the value from there fixes beam's scaling issues
                var boxPointUp = boxOfCurrBox.up;
                var boxPointRight = boxOfCurrBox.right;
                var boxPointCenter = boxOfCurrBox.center;
                if (currBox.initHasBeenCalled)
                {
                    boxOfCurrBox = currBox.physicsBox;
                    boxPointRight  = boxOfCurrBox.right;
                    boxPointUp     = boxOfCurrBox.up;
                    boxPointCenter = boxOfCurrBox.center;
                    boxScale = (Fix)1;
                }

                // to make it approximately 500,000x more readable, there's some extra spacing so everything lines up nicely
                Vec2 boxPointUpLeft    = boxScale * (boxPointCenter + boxPointUp - boxPointRight);
                Vec2 boxPointDownLeft  = boxScale * (boxPointCenter - boxPointUp - boxPointRight);
                Vec2 boxPointUpRight   = boxScale * (boxPointCenter + boxPointUp + boxPointRight);
                Vec2 boxPointDownRight = boxScale * (boxPointCenter - boxPointUp + boxPointRight);

                HitboxVisualizerLine boxLineTop = new HitboxVisualizerLine(boxPointUpLeft, boxPointUpRight);
                HitboxVisualizerLine boxLineRight = new HitboxVisualizerLine(boxPointUpRight, boxPointDownRight);
                HitboxVisualizerLine boxLineBottom = new HitboxVisualizerLine(boxPointDownRight, boxPointDownLeft);
                HitboxVisualizerLine boxLineLeft = new HitboxVisualizerLine(boxPointDownLeft, boxPointUpLeft);
                    

                // make lines meet at the corners properly, and make their outer edge the real hitbox edge
                HitboxVisualizerLine[] box_lines = [boxLineTop, boxLineRight, boxLineBottom, boxLineLeft];
                for (int j = 0; j < box_lines.Length; j++)
                {
                    Vector2 p1 = (Vector2)box_lines[j].point1;
                    Vector2 p2 = (Vector2)box_lines[j].point2;
                    float angleRadPlus2Pi = Mathf.Atan2(p2.y - p1.y, p2.x - p1.x) + Mathf.PI/2;
                    box_lines[j].point1.x = box_lines[j].point1.x - ((Fix)drawingThickness / (Fix)2) * (Fix)Mathf.Cos((float)angleRadPlus2Pi);
                    box_lines[j].point1.y = box_lines[j].point1.y - ((Fix)drawingThickness / (Fix)2) * (Fix)Mathf.Sin((float)angleRadPlus2Pi);
                    box_lines[j].point2.x = box_lines[j].point2.x - ((Fix)drawingThickness / (Fix)2) * (Fix)Mathf.Cos((float)angleRadPlus2Pi);
                    box_lines[j].point2.y = box_lines[j].point2.y - ((Fix)drawingThickness / (Fix)2) * (Fix)Mathf.Sin((float)angleRadPlus2Pi);
                }

                newHitboxLineGroups_NoDistortion.Add(new HitboxLineGroup(
                    [
                    boxLineTop,
                    boxLineRight,
                    boxLineBottom,
                    boxLineLeft,
                    ],
                    HitboxLineGroup.pickLineStyling(currBox)));
            }

            // CALCULATE CIRCLES
            //for (int i = 0; i < inputDPhysCircleDict.Count /*- 1*/; i++)
            for (int i = 0; i < inputDPhysCircleDict.Values.ToList().Count /*- 1*/; i++)
            {
                var currCircle = inputDPhysCircleDict.Values.ToList()[i];

                if (currCircle == null)
                {
                    inputDPhysCircleDict.Remove(inputDPhysCircleDict.Keys.ToList()[i]);
                    Logger.LogWarning("inputDPhysCircleDict.Values.ToList()[i] == null, removing from array. a round probably ended.");
                    continue;
                }
                if (!currCircle.gameObject.activeSelf)
                {
                    continue;
                }

                var currCircleShape = currCircle.Circle();
                Fix circleRadius = currCircleShape.radius * currCircle.Scale;
                Fix circleX = currCircleShape.Pos().x;
                Fix circleY = currCircleShape.Pos().y;

                // for some reason the physics engine and class instance have separate varibles for the same property.
                if (currCircle.initHasBeenCalled)
                {
                    var physEngineCircleObj = DetPhysics.Get().circles.colliders[DetPhysics.Get().circles.ColliderIndex(currCircle.pp.instanceId)];
                    circleRadius = physEngineCircleObj.radius;
                    circleX      = physEngineCircleObj.Pos().x;
                    circleY      = physEngineCircleObj.Pos().y;
                }

                circleRadius -= (Fix)drawingThickness / (Fix)2;

                int circleLineAmount = circleDrawingMinAmountOfLines + (int)(circleRadius * (Fix)4);
                //Logger.LogInfo("circle radius: " + circleRadius.ToString() + " | circleLineAmount: " + circleLineAmount.ToString());
                float angleDifferencePerIteration = 360f / (float)circleLineAmount;

                List<HitboxVisualizerLine> currCircleLines = [];
                // setup the initial first position so the loop can be simpler.
                // also yeah this just simplifies down because cos(0) = 1 and sin(0) = 0.
                Vec2 nextStartingCirclePoint = new Vec2(circleX+circleRadius, circleY);

                // yes, j = 1 because nextStartingCirclePoint is already set.
                // we also add <strikethrough>+1</strikethrough> for circleDrawing_AmountOfLines because j starts at 1 instead of 0.
                // I'm not sure why +2 fixes the last points not being drawn sometimes? this is required after I made the amount of lines scale with the radius.
                for (int j = 1; j < circleLineAmount + 2; j++)
                {
                    float angle = j * angleDifferencePerIteration * Mathf.Deg2Rad;
                    
                    Vec2 newCirclePoint = new Vec2(circleX + (circleRadius * (Fix)Mathf.Cos(angle)), circleY + (circleRadius * (Fix)Mathf.Sin(angle)));
                    currCircleLines.Add(new HitboxVisualizerLine(nextStartingCirclePoint, newCirclePoint));

                    nextStartingCirclePoint = newCirclePoint;
                }

                newHitboxLineGroups_DistortionAllowed.Add(new HitboxLineGroup(currCircleLines, HitboxLineGroup.pickLineStyling(currCircle), currCircle.gameObject));
            }

            return Tuple.Create(newHitboxLineGroups_NoDistortion, newHitboxLineGroups_DistortionAllowed);
        }
    }

    [HarmonyPatch(typeof(DPhysicsBox))]
    class Patch_AddDPhysBoxBoundsTracking
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(DPhysicsBox.Initialize))]
        [HarmonyPatch(nameof(DPhysicsBox.Init))]
        [HarmonyPatch(nameof(DPhysicsBox.ManualInit))]
        [HarmonyPatch(nameof(DPhysicsBox.Awake))]
        static void Postfix(DPhysicsBox __instance)
        {
            var instanceID = __instance.GetInstanceID();

            if (Plugin.DPhysBoxDict.ContainsKey(instanceID))
            {
                return;
            }
            //__instance.gameObject.
            Plugin.Logger.LogInfo("adding instanceID DPhysicsBox to list:" + instanceID);
            Plugin.DPhysBoxDict.Add(instanceID, __instance);
        }
    }

    [HarmonyPatch(typeof(DPhysicsCircle))]
    class Patch_AddDPhysCircleBoundsTracking
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(DPhysicsCircle.Initialize))]
        [HarmonyPatch(nameof(DPhysicsCircle.Init))]
        [HarmonyPatch(nameof(DPhysicsCircle.ManualInit))]
        [HarmonyPatch(nameof(DPhysicsCircle.Awake))]
        static void Postfix(DPhysicsCircle __instance)
        {
            var instanceID = __instance.GetInstanceID();

            if (Plugin.DPhysCircleDict.ContainsKey(instanceID))
            {
                return;
            }

            Plugin.Logger.LogInfo("adding instanceID DPhysicsCircle to list:" + instanceID);
            Plugin.DPhysCircleDict.Add(instanceID, __instance);
        }
    }

    // note leftovers will get cleaned up by other code that will delete null instances if they end up in the dict anyway
    // mainly that seems to happen at the end of rounds but in case this function either didn't exist or wasn't working everything would still get cleaned eventually
    [HarmonyPatch(typeof(MonoUpdatable))]
    class Patch_MonoUpdatable_AddDPhysObjDeleteHook
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(DPhysicsBox.OnDestroyUpdatable))]
        [HarmonyPatch(nameof(DPhysicsCircle.OnDestroyUpdatable))]
        static void Postfix_addPhysBoxDelHook(MonoUpdatable __instance)
        {
            if (__instance == null)
            {
                return;
            }
            var instanceClassName = __instance.GetScriptClassName();

            if (instanceClassName == "DPhysicsBox")
            {
                Plugin.DPhysBoxDict.Remove(__instance.GetInstanceID());
                var instanceID = __instance.GetInstanceID();
                Plugin.Logger.LogInfo("removing instanceID DPhysicsBox from list:" + instanceID);
            }
            else if (instanceClassName == "DPhysicsCircle")
            {
                Plugin.DPhysCircleDict.Remove(__instance.GetInstanceID());
                var instanceID = __instance.GetInstanceID();
                Plugin.Logger.LogInfo("removing instanceID DPhysicsCircle from list:" + instanceID);
            }
        }
    }

    [HarmonyPatch(typeof(DetPhysics))]
    class Patch_HitboxViewer_DetPhysicsRaycastHooks
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(DetPhysics.RaycastAll))]
        static void Postfix_DetPhysicsRaycastAll(Vec2 origin, Vec2 dir, Fix distance)
        {
            Plugin.DebugLineGroup.AddLine(new HitboxVisualizerLine(origin, dir, distance, Plugin.debugLineLifetime));
        }
    }

    // TODO: possibly use a transpiler to patch out whatever draws all of the moving airbone player tracking lines and just continue patching Debug.DrawLine()
    // alternatively, I can also manually patch every function that uses Debug.DrawLine() and categorize them from there, but that would be a lot of work.
    // either way it'll be annoying.
    // hmmmmmmm... ok look if I need to visualize something in specific I'll just add it manually.

    // while i can patch Debug.DrawLine() directly because they have C# wrappers, the lines that follow moving airbone players cover the screen way too much.
    // [HarmonyPatch(typeof(Debug))]
    // class Patch_HitboxViewer_DebugDraw
    // {
    //     [HarmonyPostfix]
    //     [HarmonyPatch(nameof(Debug.DrawLine), new[] { typeof(Vector3), typeof(Vector3) } )]
    //     [HarmonyPatch(nameof(Debug.DrawLine), new[] { typeof(Vector3), typeof(Vector3), typeof(Color) })]
    //     static void Postfix_DebugDrawLine(Vector3 start, Vector3 end)
    //     {
    //         Plugin.DebugLineGroup.AddLine(new HitboxVisualizerLine((Vec2)start, (Vec2)end, Plugin.debugLineLifetime));
    //     }

    //     [HarmonyPatch(nameof(Debug.DrawLine), new[] { typeof(Vector3), typeof(Vector3), typeof(Color), typeof(float) })]
    //     [HarmonyPatch(nameof(Debug.DrawLine), new[] { typeof(Vector3), typeof(Vector3), typeof(Color), typeof(float), typeof(bool) })]
    //     static void Postfix_DebugDrawLineWithDuration(Vector3 start, Vector3 end, float duration)
    //     {
    //         // movement visualization stuff tries to draw a bunch of lines every frame
    //         float lifetime = Plugin.debugLineLifetime;
    //         if (duration < lifetime) {
    //             lifetime = duration;
    //         }
    //         Plugin.DebugLineGroup.AddLine(new HitboxVisualizerLine((Vec2)start, (Vec2)end, lifetime));
    //     }
    // }

    // making a transpiler patch would be a ton of effort, so I'll just do this instead.
    [HarmonyPatch(typeof(DetPhysics))]
    class DetPhysicsPatches
    {
        [HarmonyPrefix]
        [HarmonyPatch(nameof(DetPhysics.SimulateRopes_parallel))]
        public static void SimulateRopes_parallel_hook()
        {
            for (int i = 0; i < 10; i++)
            {
                Plugin.ManualLineGroups[i].groupLines.Clear();
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(DetPhysics.SimulateRope))]
        public static void SimulateRope_functionHook(/*DetPhysics __instance, */LevelType ___levelTypeThisFrame, RopeBody body)
        {
            List<Vec2> segment_copy = body.segment.ToList();
            List<Vec2> segmentPrev_copy = body.segmentPrev.ToList();
            if (!body.enabled || body.timeInAir_end == (Fix)100)
            {
                return;
            }
            Vec2 vec;
            Vec2 vec2;
            Fix massStartOr1;
            Fix massEndOr1;
            if (body.hookHasArrived)
            {
                vec = body.force_start;
                vec2 = body.force_end;
                massStartOr1 = body.mass_start;
                massEndOr1 = body.mass_end;
            }
            else
            {
                vec = Vec2.zero;
                vec2 = Vec2.zero;
                massStartOr1 = Fix.One;
                massEndOr1 = Fix.One;
            }
            Vec2 vec3 = Vec2.down * DetPhysics.Get().ropeGravity * ((___levelTypeThisFrame == LevelType.space) ? ((Fix)0.5) : Fix.One);
            int num = body.segmentCount - 1;
            Vec2 vec4 = segment_copy[0];
            segment_copy[0] = (Fix)2L * segment_copy[0] - segmentPrev_copy[0] + GameTime.FixedTimeStep * vec;
            segmentPrev_copy[0] = vec4;
            _ = Fix.One / (Fix)num;
            for (int i = 1; i < num; i++)
            {
                Vec2 vec5 = segment_copy[i];
                segment_copy[i] = (Fix)2L * segment_copy[i] - segmentPrev_copy[i] + GameTime.FixedTimeStep * vec3;
                segmentPrev_copy[i] = vec5;
            }
            Vec2 vec6 = segment_copy[num];
            segment_copy[num] = (Fix)2L * segment_copy[num] - segmentPrev_copy[num] + GameTime.FixedTimeStep * vec2;
            segmentPrev_copy[num] = vec6;
            
            for (int foo = 0; foo < body.segmentCount - 2; foo++)
            {
                Plugin.ManualLineGroups[0].lineGroupStyle = lineDrawingStyle.debugDefault;
                Plugin.ManualLineGroups[0].AddLine(new HitboxVisualizerLine(segment_copy[foo], segment_copy[foo + 1]));
            }

            Fix oneHalf = (Fix)0.5f;
            for (int j = 0; j < DetPhysics.Get().ropeRelaxationIterations; j++)
            {
                Vec2 vec7 = segment_copy[1] - segment_copy[0];
                Fix vec7Length = Vec2.Magnitude(vec7);
                if (vec7Length == Fix.Zero)
                {
                    continue;
                }
                Fix fix5 = -vec7Length;
                Vec2 vec7_normalized = vec7 / vec7Length;
                segment_copy[1] += fix5 * vec7_normalized * massStartOr1;
                segment_copy[0] -= fix5 * vec7_normalized * (Fix.One - massStartOr1);
                for (int k = 1; k < body.segmentCount - 2; k++)
                {
                    vec7 = segment_copy[k] - segment_copy[k + 1];
                    vec7Length = Vec2.Magnitude(vec7);
                    if (!(vec7Length == Fix.Zero))
                    {
                        fix5 = body.segmentSeparation - vec7Length;
                        vec7_normalized = vec7 / vec7Length;
                        segment_copy[k] += fix5 * vec7_normalized * oneHalf;
                        segment_copy[k + 1] -= fix5 * vec7_normalized * oneHalf;
                    }
                }
                vec7 = segment_copy[body.segmentCount - 2] - segment_copy[body.segmentCount - 1];
                vec7Length = Vec2.Magnitude(vec7);
                if (!(vec7Length == Fix.Zero))
                {
                    fix5 = -vec7Length;
                    vec7_normalized = vec7 / vec7Length;
                    segment_copy[body.segmentCount - 2] += fix5 * vec7_normalized * massEndOr1;
                    segment_copy[body.segmentCount - 1] -= fix5 * vec7_normalized * (Fix.One - massEndOr1);
                }
                for (int baz = 0; baz < body.segmentCount - 2; baz++)
                {
                    Plugin.ManualLineGroups[j + 2].AddLine(new HitboxVisualizerLine(segment_copy[baz], segment_copy[baz + 1], RedTransparent));
                }
            }






            // TODO: add back in the platform collison stuff that's here in the original function






            for (int bar = 0; bar < body.segmentCount - 2; bar++)
            {
                Plugin.ManualLineGroups[1].AddLine(new HitboxVisualizerLine(segment_copy[bar], segment_copy[bar + 1], GreenTransparent));
            }
        }
    }

    public class ListOfLineHolderGameObjs
    {
        public List<GameObject> gameObjsList = new List<GameObject>();
        public int minCapacity = 100;
        public int currUsedAmountOfGameObjs = 0;
        public Material lineMaterial = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended"));

        public void AddGameObjects(int amount)
        {
            for (int i = 0; i < amount; i++)
            {
                var currGameObj = new GameObject();
                GameObject.DontDestroyOnLoad(currGameObj);
                LineRenderer lineRenderer = currGameObj.AddComponent(typeof(LineRenderer)) as LineRenderer;

                lineRenderer.material = lineMaterial;
                lineRenderer.sortingLayerID = SortingLayer.NameToID("PostProcessing");
                lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                gameObjsList.Add(currGameObj);
            }
        }

        public void SetAllLineRendererMaterials(Material newMaterial)
        {
            for (int i = 0; i < gameObjsList.Count; i++)
            {
                gameObjsList[i].TryGetComponent(out LineRenderer lineRenderer);
                lineRenderer.material = newMaterial;
            }
        }

        public ListOfLineHolderGameObjs(bool useLineDrawingClassMaterial = true)
        {
            if (useLineDrawingClassMaterial)
            {
                lineMaterial = LineDrawing.lineRendererBaseMaterial;
            }
            AddGameObjects(minCapacity);
        }

        public void SetLineRendererPropsAt(int objListIndex, Vector3 startPos, Vector3 endPos, float thickness, Color lineColor/*, bool objIsActive = true*/)
        {
            var currGameObj = gameObjsList[objListIndex];
            currGameObj.TryGetComponent(out LineRenderer lineRenderer);
            lineRenderer.SetPositions([startPos, endPos]);
            lineRenderer.startWidth = thickness;
            lineRenderer.endWidth   = thickness;
            lineRenderer.startColor = lineColor;
            lineRenderer.endColor   = lineColor;
            lineRenderer.forceRenderingOff = false;
        }

        public void DeleteGameObjectsAfterIndex(int baseIndex)
        {
            var amountOfObjsToDelete = gameObjsList.Count - baseIndex;
            for (int i = 0; i < amountOfObjsToDelete; i++)
            {
                var currGameObj = gameObjsList[baseIndex];
                gameObjsList.Remove(currGameObj);
                GameObject.Destroy(currGameObj);

            }
        }

        public void CleanUpOldLineRendererPositionsFromGameObjsAfter(int startIndex)
        {
            for (int i = startIndex; i < gameObjsList.Count; i++)
            {
                if (gameObjsList[i].TryGetComponent(out LineRenderer lineRenderer))
                {
                    lineRenderer.SetPositions([new Vector3(0, 0), new Vector3(0, 0)]);
                    lineRenderer.forceRenderingOff = true;
                }
            }
        }

        public void CleanUpAllLineRenderers()
        {
            for (int i = 0; i < gameObjsList.Count; i++)
            {
                gameObjsList[i].TryGetComponent(out LineRenderer lineRenderer);
                lineRenderer.SetPositions((Vector3[])[]);
                lineRenderer.forceRenderingOff = true;
            }
        }
    }

    public struct hitboxVisualizerLineStyling
    {
        // these are constant but the values in drawingStyleToLineColors get overwritten when the config's color settings load.
        // if the config is invalid these'll be used as a fallback.
        public static Color RedColor     = new Color(1, 0, 0, 0.8f);
        public static Color BlueColor    = new Color(0, 0, 1, 0.8f);
        public static Color GreenColor   = new Color(0, 1, 0, 0.8f);
        public static Color YellowColor  = new Color(1, 0.92f, 0.016f, 0.8f);
        public static Color MagentaColor = new Color(1, 0, 1, 0.8f);
        public static Color WhiteColor   = new Color(1, 1, 1, 0.8f);
        public static Color BlackColor   = new Color(0, 0, 0, 0.8f);

        public static Color DarkGrey     = new Color(0.2f, 0.2f, 0.2f, 1f);
        public static Color lightGrey    = new Color(0.8f, 0.8f, 0.8f, 1f);

        public static Color YellowTransparent  = new Color(1, 0.92f, 0.016f, 0.5f);
        public static Color GreenTransparent = new Color(0, 1, 0, 0.5f);
        public static Color RedTransparent = new Color(1, 0, 0, 0.5f);

        public static Color transparent = new Color(1f, 1f, 1f, 0.4f);
        
        public enum lineDrawingStyle
        {
            defaultColors,
            disabledPhys,
            circleColors,
            debugDefault,
            /// <summary>
            /// use `lineDrawingStyle.manual` when you want to assign all of the line colors manually.
            /// </summary>
            manual 
            /*,
            UpdateWithoutLateUpdate*/
        }
        public static Dictionary<lineDrawingStyle, List<Color>> drawingStyleToLineColors = new Dictionary<lineDrawingStyle, List<Color>>
        {
            {lineDrawingStyle.defaultColors, [RedColor, YellowColor, GreenColor, BlueColor, MagentaColor]},
            {lineDrawingStyle.disabledPhys, [BlackColor, WhiteColor]},
            {lineDrawingStyle.circleColors, [RedColor, YellowColor, GreenColor, BlueColor, MagentaColor]},
            {lineDrawingStyle.debugDefault, [MagentaColor, lightGrey, DarkGrey]},
            {lineDrawingStyle.manual, []}
            /*{lineDrawingStyle.UpdateWithoutLateUpdate, [MagentaColor, MagentaColor, MagentaColor, MagentaColor] }*/
        };
    }

    public class HitboxVisualizerLine
    {
        public Vec2 point1;
        public Vec2 point2;
        public Color lineColor;
        // ideally i would put this in a subclass but doing that would mean I also couldn't use all the stuff built around HitboxLineGroup for those lines
        public bool hasLifeTimeout = false;
        public float lifetimeSeconds = 1.5f; // same length as XGunExplosion, thus lasting as long as the explosion hitbox does visually.

        public HitboxVisualizerLine(Vec2 point_1, Vec2 point_2)
        {
            point1 = point_1;
            point2 = point_2;
        }
        public HitboxVisualizerLine(Vec2 point_1, Vec2 point_2, Color color) : this(point_1, point_2)
        {
            lineColor = color;
        }
        public HitboxVisualizerLine(Vec2 point_1, Vec2 point_2, Color color, float drawSeconds_) : this(point_1, point_2, drawSeconds_)
        {
            lineColor = color;
        }
        public HitboxVisualizerLine(Vec2 point_1, Vec2 dir, Fix distance)
        {
            point1 = point_1;
            point2 = point_1 + dir * distance;
        }
        public HitboxVisualizerLine(Vec2 point_1, Vec2 point_2, float drawSeconds_) : this(point_1, point_2)
        {
            lifetimeSeconds = drawSeconds_;
            hasLifeTimeout = true;
        }
        public HitboxVisualizerLine(Vec2 point_1, Vec2 dir, Fix distance, float drawSeconds_) : this(point_1, dir, distance)
        {
            lifetimeSeconds = drawSeconds_;
            hasLifeTimeout = true;
        }
    }
    // because the lineRenderer implementation uses SetPositions() every time it's called to update and SetPositions() removes the old value (aka what a setter does),
    // if the lines aren't grouped together per object, the lines that are drawn later will completely overwrite the old lines for that object.
    // this means we either can only have 1 line per object or have to give it a list of lines so it can SetPositions() with all of the lines as 1 set.
    // (later)
    // BUT THEN, APPARENLTLY LINERENDERER IS ACTUALLY TERRIBLE AT DRAWING LINES!
    // ANYTHING MORE COMPLEX THAN JUST POINT A TO POINT B CREATES HIGHLY UGLY DISTORTION!
    public class HitboxLineGroup
    {
        public List<HitboxVisualizerLine> groupLines;
        public GameObject parentGameObj;
        public lineDrawingStyle lineGroupStyle;

        public HitboxLineGroup(List<HitboxVisualizerLine> hitboxLines, lineDrawingStyle lineStyle, GameObject parentGameObject)
        {
            if (parentGameObject == null)
            {
                Plugin.Logger.LogInfo("hitboxLineGroup: parentGameObject == null");
                return;
            }
            groupLines = hitboxLines;
            parentGameObj = parentGameObject;
            lineGroupStyle = lineStyle;
            UpdateLineColorsToMatchStyle(lineStyle);
        }

        public HitboxLineGroup(List<HitboxVisualizerLine> hitboxLines, lineDrawingStyle lineStyle)
        {
            groupLines = hitboxLines;
            parentGameObj = null;
            lineGroupStyle = lineStyle;
            UpdateLineColorsToMatchStyle(lineStyle);
        }

        public HitboxLineGroup(List<HitboxVisualizerLine> hitboxLines)
        {
            groupLines = hitboxLines;
            parentGameObj = null;
            lineGroupStyle = lineDrawingStyle.manual;
        }

        public void AddLine(HitboxVisualizerLine line)
        {
            groupLines.Add(line);
            UpdateOneLineColorToMatchStyle(lineGroupStyle, groupLines.Count - 1);
        }

        public void TickLineLifetimes(float deltaSeconds)
        {
            for (int i = 0; i < groupLines.Count; i++)
            {
                if (groupLines[i].hasLifeTimeout)
                {
                    groupLines[i].lifetimeSeconds -= deltaSeconds;
                    if (groupLines[i].lifetimeSeconds <= 0)
                    {
                        groupLines.RemoveAt(i);
                    }
                }
            }
        }

        public static lineDrawingStyle pickLineStyling(IPhysicsCollider DPhysObj)
        {
            if (!DPhysObj.enabled)
            {
                return lineDrawingStyle.disabledPhys;
            }
            if (DPhysObj is DPhysicsCircle)
            {
                return lineDrawingStyle.circleColors;
            }
            return lineDrawingStyle.defaultColors;
        }

        public void UpdateOneLineColorToMatchStyle(lineDrawingStyle lineStyle, int lineIndex)
        {
            if (lineStyle == lineDrawingStyle.manual)
            {
                return;
            }
            var lineStyleColors = drawingStyleToLineColors[lineStyle];
            groupLines[lineIndex].lineColor = lineStyleColors[(groupLines.Count - 1) % (lineStyleColors.Count)];
        }

        public void UpdateLineColorsToMatchStyle(lineDrawingStyle lineStyle)
        {
            if (lineStyle == lineDrawingStyle.manual)
            {
                return;
            }
            lineGroupStyle = lineStyle;
            var colorIndex = 0;
            var lineColors = drawingStyleToLineColors[lineGroupStyle];
            for (int i = 0; i < groupLines.Count; i++)
            {
                if (colorIndex > lineColors.Count - 1)
                {
                    colorIndex = 0;
                }
                var currLine = groupLines[i];

                currLine.lineColor = lineColors[colorIndex];

                colorIndex++;
            }
        }

        public Vector3[] GetListOfComponentPoints()
        {
            List<Vector3> linePointsArray = [];

            linePointsArray.Add(new Vector3((float)groupLines[0].point1.x, (float)groupLines[0].point1.y, 0));

            for (int i = 0; i < groupLines.Count-1; i++)
            {
                var currLine = groupLines[i];
                linePointsArray.Add(new Vector3((float)currLine.point1.x, (float)currLine.point1.y, 0));
                //linePointsArray.Add(new Vector3((float)currLine.point2.x, (float)currLine.point2.y, 0));
            }
            return linePointsArray.ToArray();
        }

        public Gradient GetLineGradientForLineColors()
        {
            List<GradientColorKey> lineGradientColors = [];
            List<GradientAlphaKey> lineGradientAlphas = [];

            var lineColors = drawingStyleToLineColors[lineGroupStyle];
            float percentOfLinePerOneColorSegment = 1f / (float)lineColors.Count();

            for (int i = 0; i < lineColors.Count(); i++)
            {
                float gradientLinePos = i * percentOfLinePerOneColorSegment;
                lineGradientColors.Add(new GradientColorKey(lineColors[i], gradientLinePos));
                lineGradientAlphas.Add(new GradientAlphaKey(lineColors[i].a, gradientLinePos));
            }
            lineGradientColors.Add(new GradientColorKey(lineColors[0], 1));
            lineGradientAlphas.Add(new GradientAlphaKey(lineColors[0].a, 1));

            Gradient lineGradient = new Gradient();
            lineGradient.SetKeys(lineGradientColors.ToArray(), lineGradientAlphas.ToArray() );
            lineGradient.mode = GradientMode.PerceptualBlend;
            return lineGradient;
        }
    }

    public class LineDrawing
    {
        public static Material lineRendererBaseMaterial = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended"));

        public static void setUpLineRendererMaterialToDefault()
        {
            lineRendererBaseMaterial = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended"));
            lineRendererBaseMaterial.SetOverrideTag("RenderType", "Fade");
            lineRendererBaseMaterial.renderQueue = 5000;
            lineRendererBaseMaterial.SetInt("_ZWrite", 1);
            lineRendererBaseMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        }

        public static void drawLinesAsLineRendererPositions(List<HitboxLineGroup> lineGroups)
        {
            for (int j = 0; j < lineGroups.Count; j++)
            {
                if (lineGroups[j].parentGameObj == null)
                {
                    Plugin.Logger.LogInfo("parentGameObj is null");
                    continue;
                }

                LineRenderer lineRenderer;
                if (lineGroups[j].parentGameObj.TryGetComponent<LineRenderer>(out LineRenderer curLineRederer))
                {
                    lineRenderer = curLineRederer;
                }
                else
                {
                    lineRenderer = lineGroups[j].parentGameObj.AddComponent<LineRenderer>();
                }

                Vector3[] newPositions = lineGroups[j].GetListOfComponentPoints();
                // according to the unity project as decompiled by assetRipper, PostProcessing is the highest sorting layer in v2.3.4
                lineRenderer.sortingLayerID = SortingLayer.NameToID("PostProcessing");
                lineRenderer.loop = true;
                lineRenderer.material = lineRendererBaseMaterial;
                lineRenderer.colorGradient = lineGroups[j].GetLineGradientForLineColors();
                lineRenderer.startWidth = Plugin.drawingThickness;
                lineRenderer.endWidth = Plugin.drawingThickness;
                lineRenderer.positionCount = newPositions.Length;
                lineRenderer.material.renderQueue = 4999;
                lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;


                lineRenderer.SetPositions(newPositions);
                // mainly helps *SOME OF* the remove ugly distortion caused by LineRenderer
                lineRenderer.Simplify(0);
            }
        }

        // because of how LineRenderer works, anything more complicated than a direct line from point A to B will start having really ugly distortion effects.
        // at least for stuff like rectangles. circles seem to suffer from it less and so will use the other function.
        // using LineRenderer.Simplify() and setting the line thickness set lower can help but the distortion is apparently impossible to fully remove
        // ============================================
        // so the solution is to create a bunch of LineRenderers and just use 1 line per linerenderer, as a simple point A to point B line doesn't have the distortion issue.
        // this also means that a bunch of holder child objects must be created because GameObject can only hold 1 LineRenderer at a time.

        // DebugLines also use this function as one large line group because lines here don't need to be connected.
        public static void drawLinesIndividuallyWithHolderGameObjects(List<HitboxLineGroup> lineGroups)
        {
            int amountOfUsedHolderObjs = 0;
            ListOfLineHolderGameObjs holderGameObjs = Plugin.poolOfLineHolderGameObjs;

            for (int i = 0; i < lineGroups.Count; i++) {
                var linesList = lineGroups[i].groupLines;

                if (amountOfUsedHolderObjs + linesList.Count > holderGameObjs.gameObjsList.Count)
                {
                    holderGameObjs.AddGameObjects(amountOfUsedHolderObjs + linesList.Count - holderGameObjs.gameObjsList.Count);
                }
                for (int j = 0; j < linesList.Count; j++)
                {
                    var line = linesList[j];
                    holderGameObjs.SetLineRendererPropsAt(amountOfUsedHolderObjs, (Vector3)line.point1, (Vector3)line.point2, Plugin.drawingThickness, line.lineColor);
                    amountOfUsedHolderObjs++;
                }
            }
            // clean up any unused game objects
            if (amountOfUsedHolderObjs < holderGameObjs.gameObjsList.Count)
            {
                if (amountOfUsedHolderObjs > holderGameObjs.minCapacity)
                {
                    holderGameObjs.DeleteGameObjectsAfterIndex(amountOfUsedHolderObjs);
                }
                else
                {
                    holderGameObjs.DeleteGameObjectsAfterIndex(holderGameObjs.minCapacity);
                }
            }
            // clear LineRenderer positions on any unused gameObjects, so that we don't get old lines still displaying on screen.
            holderGameObjs.CleanUpOldLineRendererPositionsFromGameObjsAfter(amountOfUsedHolderObjs);
            //Plugin.Logger.LogInfo("amount of GameObjects being used: " + holderGameObjs.gameObjsList.Count);
        }
    }
}
