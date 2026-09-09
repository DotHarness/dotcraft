using System;
using System.Threading;
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Runtime.InteropServices;

namespace DotCraft.Unity
{
    public static class Entry
    {
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        static int pending;
        public static void Initialize(string evidencePath)
        {
            if (Interlocked.Exchange(ref pending, 1) != 0) return;
            var bootstrapThread = GetCurrentThreadId();
            EditorApplication.CallbackFunction callback = null;
            callback = () =>
            {
                Interlocked.Exchange(ref pending, 0);
                EditorApplication.update -= callback;
                System.Reflection.Assembly.LoadFrom(Path.GetFullPath(Path.Combine(
                    Path.GetDirectoryName(typeof(EditorApplication).Assembly.Location), "../Newtonsoft.Json.dll")));
                Bridge.Start(evidencePath, bootstrapThread, GetCurrentThreadId());
            };
            EditorApplication.CallbackFunction previous;
            do { previous = EditorApplication.update; }
            while (Interlocked.CompareExchange(ref EditorApplication.update,
                (EditorApplication.CallbackFunction)Delegate.Combine(previous, callback), previous) != previous);
        }
        public static string Ping()
        {
            return "dotcraft-attach-ok|" + Application.unityVersion + "|" + Application.dataPath
                + "|domain=" + AppDomain.CurrentDomain.FriendlyName + "|thread=" + Thread.CurrentThread.ManagedThreadId
                + "|playing=" + EditorApplication.isPlaying + "|compiling=" + EditorApplication.isCompiling
                + "|updating=" + EditorApplication.isUpdating;
        }
    }
}
