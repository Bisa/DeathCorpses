{
  description = "DeathCorpses - Vintage Story mod";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
    flake-utils.url = "github:numtide/flake-utils";
  };

  outputs = { self, nixpkgs, flake-utils }:
    flake-utils.lib.eachDefaultSystem (system:
      let
        pkgs = nixpkgs.legacyPackages.${system};

        modInfo = builtins.fromJSON (builtins.readFile ./modinfo.json);
        modId = modInfo.modid;
        modBaseVersion = modInfo.version;

        targetInfo = builtins.fromJSON (builtins.readFile ./targets.json);

        # Append git short hash for local dirty builds to distinguish from clean releases.
        nixVersion =
          if self ? dirtyShortRev then "${modBaseVersion}+${self.dirtyShortRev}"
          else modBaseVersion;

        # .NET SDK for a given target
        sdkFor = t:
          if t.targetFramework == "net10.0" then pkgs.dotnet-sdk_10
          else pkgs.dotnet-sdk_8;

        # Extract only the VS DLLs needed to compile against a given VS version
        vsLibsFor = t: pkgs.runCommand "vintage-story-libs-${t.gameVersion}" { } ''
          mkdir -p $out/Lib
          cd $out
          tar -xzf ${pkgs.fetchurl {
            url = "https://cdn.vintagestory.at/gamefiles/${t.channel}/vs_server_linux-x64_${t.gameVersion}.tar.gz";
            hash = t.hash;
          }} --wildcards \
            'VintagestoryAPI.dll' \
            'VintagestoryLib.dll' \
            'Lib/Newtonsoft.Json.dll' \
            'Lib/protobuf-net.dll'
        '';

        net8 = targetInfo.net8;
        net10 = targetInfo.net10;

        vsLibsNet8 = vsLibsFor net8;

        # Thin loader DLL (net8): the only ModSystem VS sees. Impl assemblies are embedded as
        # manifest resources so the loader never needs Assembly.Location or the file system.
        loaderDll = pkgs.buildDotnetModule {
          pname = "${modId}-loader";
          version = nixVersion;
          src = ./.;
          projectFile = "Loader/Loader.csproj";
          nugetDeps = ./Loader/deps.json;
          dotnet-sdk = pkgs.dotnet-sdk_8;
          selfContained = false;
          preBuild = ''
            export VintageStoryInstallDir="${vsLibsNet8}"
            # Stage impl assemblies as .impl files for EmbeddedResource pickup by dotnet build
            cp ${implNet8}/${modId}-net8.dll   Loader/${modId}-net8.impl
            cp ${implNet10}/${modId}-net10.dll  Loader/${modId}-net10.impl
          '';
          installPhase = ''
            runHook preInstall
            mkdir -p $out
            cp bin/${modId}/linux-x64/${modId}.dll $out/
            runHook postInstall
          '';
        };

        # Impl DLL compiled against a specific VS/dotnet target
        mkImpl = targetKey: implSuffix:
          let
            t = targetInfo.${targetKey};
            vsLibs = vsLibsFor t;
          in
          pkgs.buildDotnetModule {
            pname = "${modId}-impl-${implSuffix}";
            version = nixVersion;
            src = ./.;
            projectFile = "${modId}.csproj";
            nugetDeps = ./deps/${t.targetFramework}.json;
            dotnet-sdk = sdkFor t;
            selfContained = false;
            nativeBuildInputs = [ pkgs.jq ];
            dotnetBuildFlags = [ "/p:AssemblyName=${modId}-${implSuffix}" ];
            postPatch = ''
              substituteInPlace ${modId}.csproj \
                --replace-fail 'net8.0' '${t.targetFramework}'
            '';
            preBuild = ''
              export VintageStoryInstallDir="${vsLibs}"
              jq '.dependencies.game = "${t.gameVersion}"' \
                modinfo.json > modinfo.tmp && mv modinfo.tmp modinfo.json
            '';
            installPhase = ''
              runHook preInstall
              mkdir -p $out
              cp -r bin/${modId}/linux-x64/. $out/
              runHook postInstall
            '';
          };

        implNet8 = mkImpl "net8" "net8";
        implNet10 = mkImpl "net10" "net10";

      in
      {
        # Build the single combined zip (default target)
        packages.default = self.packages.${system}.zip;

        packages.zip =
          pkgs.runCommand "${modId}-${nixVersion}.zip" { buildInputs = [ pkgs.zip ]; } ''
            mkdir -p staging

            # Static assets, modinfo (patched for minimum VS version), icon — from net8 impl
            cp -r ${implNet8}/. staging/
            # Remove build artefacts; impl assemblies are loaded by the loader at runtime
            rm -f staging/*.dll staging/*.deps.json staging/*.pdb staging/*.runtimeconfig.json

            # Loader DLL (contains embedded net8 + net10 impl assemblies)
            cp ${loaderDll}/${modId}.dll staging/

            # Standalone impl binaries as fallback — some VS versions strip manifest
            # resources when loading mod DLLs from bytes
            cp ${implNet8}/${modId}-net8.dll   staging/${modId}-net8.bin
            cp ${implNet10}/${modId}-net10.dll  staging/${modId}-net10.bin

            cd staging
            find . | sort | zip -X -@ $out
          '';

        # Usage: nix build .#fetch-deps-net8 && ./result ./deps/net8.0.json
        packages.fetch-deps-net8 = implNet8.passthru.fetch-deps;
        # Usage: nix build .#fetch-deps-net10 && ./result ./deps/net10.0.json
        packages.fetch-deps-net10 = implNet10.passthru.fetch-deps;
      }
    );
}
