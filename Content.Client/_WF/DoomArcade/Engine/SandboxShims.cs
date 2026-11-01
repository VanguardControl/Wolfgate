#nullable disable
// Copyright (C) 2019-2020 Nobuaki Tanaka
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using System.Linq;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.Client._WF.DoomArcade.Engine
{
    /// <summary>Discards the engine's startup log; System.Console is not in the sandbox.</summary>
    internal static class Console
    {
        public static void Write(string value) { }
        public static void WriteLine(string value) { }
    }

    /// <summary>Filename helpers; System.IO.Path is not in the sandbox.</summary>
    internal static class Path
    {
        public static string Combine(string a, string b) => a + "/" + b;

        public static string GetFileName(string path) => path.Substring(path.LastIndexOf('/') + 1);

        public static string GetExtension(string path)
        {
            var name = GetFileName(path);
            var dot = name.LastIndexOf('.');
            return dot < 0 ? "" : name.Substring(dot);
        }

        public static string GetFileNameWithoutExtension(string path)
        {
            var name = GetFileName(path);
            var dot = name.LastIndexOf('.');
            return dot < 0 ? name : name.Substring(0, dot);
        }
    }

    /// <summary>Keeps save games in the player's user data; System.IO.File is not in the sandbox.</summary>
    internal static class File
    {
        private static readonly ResPath Folder = new ResPath("/wolfgate_games/doom");

        /// <summary>Set by the running session, which is the only thing that saves or loads.</summary>
        public static IWritableDirProvider Store;

        public static bool Exists(string path) => Store != null && Store.Exists(Folder / Path.GetFileName(path));

        public static byte[] ReadAllBytes(string path) => Store.ReadAllBytes(Folder / Path.GetFileName(path));

        public static IEnumerable<string> ReadLines(string path) => Enumerable.Empty<string>();

        public static void WriteAllBytes(string path, byte[] data, int length)
        {
            Store.CreateDir(Folder);
            using var file = Store.OpenWrite(Folder / Path.GetFileName(path));
            file.Write(data, 0, length);
        }
    }

    /// <summary>A pair of values; System.Tuple is not in the sandbox.</summary>
    public sealed class Tuple<T1, T2>
    {
        public readonly T1 Item1;
        public readonly T2 Item2;

        public Tuple(T1 item1, T2 item2)
        {
            Item1 = item1;
            Item2 = item2;
        }
    }

    public static class Tuple
    {
        public static Tuple<T1, T2> Create<T1, T2>(T1 item1, T2 item2) => new Tuple<T1, T2>(item1, item2);
    }

    /// <summary>Stands in for the upstream helper that located files beside the executable.</summary>
    internal static class ConfigUtilities
    {
        public static string GetExeDirectory() => "doom";
    }
}
