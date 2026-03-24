using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace DeathCorpses
{
    public class Loader : ModSystem
    {
        private List<ModSystem> _systems = [];

        public override double ExecuteOrder() => 0.00001;

        public override void StartPre(ICoreAPI api)
        {
            var implAssembly = LoadImpl();
            _systems = CreateSystems(implAssembly, api);
            foreach (var s in _systems) s.StartPre(api);
        }

        public override void Start(ICoreAPI api)
        {
            foreach (var s in _systems) s.Start(api);
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            foreach (var s in _systems) s.StartServerSide(api);
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            foreach (var s in _systems) s.StartClientSide(api);
        }

        public override void AssetsLoaded(ICoreAPI api)
        {
            foreach (var s in _systems) s.AssetsLoaded(api);
        }

        public override void AssetsFinalize(ICoreAPI api)
        {
            foreach (var s in _systems) s.AssetsFinalize(api);
        }

        public override void Dispose()
        {
            foreach (var s in _systems) s.Dispose();
        }

        private Assembly LoadImpl()
        {
            string resourceName = Environment.Version.Major >= 10
                ? "deathcorpses-net10.bin"
                : "deathcorpses-net8.bin";

            Mod.Logger.Notification($"LoadImpl: looking for '{resourceName}' (.NET {Environment.Version})");

            // Strategy 1: embedded manifest resource (works when the runtime preserves them)
            var names = typeof(Loader).Assembly.GetManifestResourceNames();
            Mod.Logger.Notification($"LoadImpl: embedded resources: [{string.Join(", ", names)}]");
            var stream = typeof(Loader).Assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                Mod.Logger.Notification("LoadImpl: loading from embedded resource");
                using (stream)
                using (var ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    return Assembly.Load(ms.ToArray());
                }
            }

            // Strategy 2: load from the mod's source (zip or extracted folder).
            // VS 1.22+ may strip manifest resources when loading DLLs from bytes.
            string sourcePath = Mod.SourcePath;
            Mod.Logger.Notification($"LoadImpl: source path = '{sourcePath}'");

            if (File.Exists(sourcePath))
            {
                Mod.Logger.Notification("LoadImpl: opening source as zip");
                using var fs = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
                var entry = zip.GetEntry(resourceName);
                if (entry != null)
                {
                    Mod.Logger.Notification("LoadImpl: loading from zip entry");
                    using var entryStream = entry.Open();
                    using var ms = new MemoryStream();
                    entryStream.CopyTo(ms);
                    return Assembly.Load(ms.ToArray());
                }
            }
            else if (Directory.Exists(sourcePath))
            {
                var filePath = Path.Combine(sourcePath, resourceName);
                Mod.Logger.Notification($"LoadImpl: looking for file '{filePath}'");
                if (File.Exists(filePath))
                {
                    Mod.Logger.Notification("LoadImpl: loading from folder");
                    return Assembly.Load(File.ReadAllBytes(filePath));
                }
            }

            throw new FileNotFoundException(
                $"Impl assembly '{resourceName}' not found. " +
                $"Source: '{sourcePath}', Embedded resources: [{string.Join(", ", names)}]");
        }

        private List<ModSystem> CreateSystems(Assembly assembly, ICoreAPI api)
        {
            var systems = new List<ModSystem>();

            // Locate the impl's registry so impl systems can look each other up
            var registryType = assembly.GetType("DeathCorpses.ModSystemRegistry");
            var registerMethod = registryType?.GetMethod("Register", BindingFlags.Public | BindingFlags.Static);

            // Use GetTypes() (not GetExportedTypes()) so internal ModSystem subclasses are found.
            // Internal visibility prevents VS from double-instantiating them when it scans the AppDomain.
            Type[] allTypes;
            try { allTypes = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { allTypes = ex.Types.Where(t => t != null).ToArray()!; }

            foreach (var type in allTypes)
            {
                if (!typeof(ModSystem).IsAssignableFrom(type) || type.IsAbstract) continue;

                var system = (ModSystem)Activator.CreateInstance(type)!;

                // Propagate loader's Mod context (logger, info, etc.) to impl systems
                typeof(ModSystem).GetProperty("Mod", BindingFlags.Public | BindingFlags.Instance)
                    ?.GetSetMethod(nonPublic: true)
                    ?.Invoke(system, new object[] { Mod });

                // Respect each impl system's own ShouldLoad decision
                if (!system.ShouldLoad(api.Side)) continue;

                registerMethod?.Invoke(null, new object[] { system });
                systems.Add(system);
            }

            systems.Sort((a, b) => a.ExecuteOrder().CompareTo(b.ExecuteOrder()));
            return systems;
        }
    }
}
