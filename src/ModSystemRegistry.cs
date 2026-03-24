using System;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace DeathCorpses
{
    public static class ModSystemRegistry
    {
        private static readonly Dictionary<Type, ModSystem> _systems = new();

        public static void Register(ModSystem system) => _systems[system.GetType()] = system;

        public static T Get<T>() where T : ModSystem => (T)_systems[typeof(T)];
    }
}
