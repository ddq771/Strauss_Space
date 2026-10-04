using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Resets the Game view's zoom to 1x whenever Play mode starts. Scrolling
/// the mouse wheel over the Game view outside Play mode zooms the view
/// itself (the "Scale" slider) - easy to do by accident in a game where the
/// wheel zooms the camera - and a zoomed Game view crops and scrolls the
/// on-screen menus off the edge. Unity has no public API for this, so it
/// goes through the GameView's internal SnapZoom; if that ever changes it
/// just does nothing.
/// </summary>
[InitializeOnLoad]
public static class GameViewScaleReset
{
    static GameViewScaleReset()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode) Reset();
        };
    }

    [MenuItem("Strauss Space/Reset Game View Zoom")]
    public static void Reset()
    {
        try
        {
            var gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            var snapZoom = gameViewType?.GetMethod("SnapZoom", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                null, new[] { typeof(float) }, null);
            if (snapZoom == null) return;
            foreach (var view in Resources.FindObjectsOfTypeAll(gameViewType))
            {
                snapZoom.Invoke(view, new object[] { 1f });
                ((EditorWindow)view).Repaint();
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("Couldn't reset the Game view zoom: " + e.Message);
        }
    }
}
