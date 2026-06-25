# GameView Screenshot

Use this reference when the user asks to capture, compare, inspect, or save the Unity GameView image through `unity_execute_csharp`.

## Rules

- Capture an already open GameView without focusing or opening windows.
- Do not call `gameView.Show()`, `gameView.Focus()`, `EditorWindow.GetWindow(..., focus: true)`, or `UnityEditorInternal.InternalEditorUtility.ShowGameView()` by default.
- If no GameView exists, return a message asking the user to open GameView or explicitly allow visible window activation.
- Prefer `UnityEditorInternal.InternalEditorUtility.CaptureEditorWindow`. Direct `GameView.RenderView` calls can return a black texture in some Editor states.
- Save and restore `RenderTexture.active` before destroying temporary render textures.

## Method Body Pattern

```csharp
var gameViewType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
if (gameViewType == null) return "GameView type not found.";

var gameView = UnityEngine.Resources.FindObjectsOfTypeAll(gameViewType)
    .OfType<UnityEditor.EditorWindow>()
    .FirstOrDefault();
if (gameView == null)
    return "No open GameView found. Ask the user to open GameView or allow visible window activation.";

var internalType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditorInternal.InternalEditorUtility");
var captureMethod = internalType == null ? null : internalType.GetMethod(
    "CaptureEditorWindow",
    System.Reflection.BindingFlags.Static |
    System.Reflection.BindingFlags.Public |
    System.Reflection.BindingFlags.NonPublic,
    null,
    new[] { typeof(UnityEditor.EditorWindow), typeof(UnityEngine.RenderTexture) },
    null);
if (captureMethod == null) return "CaptureEditorWindow(EditorWindow,RenderTexture) not found.";

var outputPath = @"C:\path\to\gameview.png";
var rect = gameView.position;
var width = System.Math.Max(1, (int)System.Math.Round(rect.width));
var height = System.Math.Max(1, (int)System.Math.Round(rect.height));
UnityEngine.RenderTexture rt = null;
UnityEngine.RenderTexture previous = UnityEngine.RenderTexture.active;
UnityEngine.Texture2D tex = null;
try
{
    rt = new UnityEngine.RenderTexture(width, height, 24, UnityEngine.RenderTextureFormat.ARGB32);
    rt.Create();
    var ok = (bool)captureMethod.Invoke(null, new object[] { gameView, rt });
    if (!ok) return "CaptureEditorWindow returned false.";

    UnityEngine.RenderTexture.active = rt;
    tex = new UnityEngine.Texture2D(width, height, UnityEngine.TextureFormat.RGB24, false);
    tex.ReadPixels(new UnityEngine.Rect(0, 0, width, height), 0, 0);
    tex.Apply(false);
    System.IO.File.WriteAllBytes(outputPath, tex.EncodeToPNG());
    return $"Saved GameView screenshot: {outputPath} ({width}x{height})";
}
finally
{
    UnityEngine.RenderTexture.active = previous;
    if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
    if (rt != null) UnityEngine.Object.DestroyImmediate(rt);
}
```

## Expected Result

The output image should match the currently visible GameView window content, including the GameView toolbar. It should not include unrelated Unity panes such as Hierarchy or Inspector.
