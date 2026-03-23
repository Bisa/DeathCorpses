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
        vsVersion = modInfo.dependencies.game;
        vsChannel = "unstable";

        # For local dirty builds, append git short hash to distinguish from a clean release.
        # CI always builds from a clean tagged commit, so modBaseVersion is used as-is.
        modVersion =
          if self ? dirtyShortRev then "${modBaseVersion}+${self.dirtyShortRev}"
          else modBaseVersion;

        vintageStoryServer = pkgs.fetchurl {
          url = "https://cdn.vintagestory.at/gamefiles/${vsChannel}/vs_server_linux-x64_${vsVersion}.tar.gz";
          hash = "sha256-sQwwQJrfVu0B/i2fiJaHUh3Jhvl4AqyzJwPRu0WL7YA=";
        };

        vsLibs = pkgs.runCommand "vintage-story-libs-${vsVersion}" { } ''
          mkdir -p $out/Lib
          cd $out
          tar -xzf ${vintageStoryServer} --wildcards \
            'VintagestoryAPI.dll' \
            'VintagestoryLib.dll' \
            'Lib/Newtonsoft.Json.dll' \
            'Lib/protobuf-net.dll'
        '';

      in
      {
        packages.default = pkgs.buildDotnetModule {
          pname = modId;
          version = modVersion;  # used for nix store path only; modinfo.json is patched in preBuild
          src = ./.;

          projectFile = "${modId}.csproj";
          nugetDeps = ./deps.json;

          dotnet-sdk = pkgs.dotnet-sdk_10;
          selfContained = false;

          # MSBuild reads env vars as MSBuild properties, satisfying the HintPaths
          # in ${modId}.csproj and the Exists() guards in Directory.Build.props.
          preBuild = ''
            export VintageStoryInstallDir="${vsLibs}"
            substituteInPlace modinfo.json \
              --replace '"version": "${modBaseVersion}"' '"version": "${modVersion}"'
          '';

          installPhase = ''
            runHook preInstall
            mkdir -p $out
            # dotnet publish puts managed output under a RID subdir (linux-x64);
            # flatten it so modinfo.json and the DLL sit at $out root.
            cp -r bin/${modId}/linux-x64/. $out/
            runHook postInstall
          '';

          meta = {
            description = "Vintage Story mod: persistent grave with waypoints";
            homepage = "https://mods.vintagestory.at/deathcorpses";
            license = pkgs.lib.licenses.mit;
          };
        };

        # Produces a .zip ready to drop into the Mods/ folder or upload to the mod portal.
        packages.zip =
          let mod = self.packages.${system}.default; in
          pkgs.runCommand "${modId}-${modVersion}.zip" { buildInputs = [ pkgs.zip ]; } ''
            cd ${mod}
            find . | sort | zip -X -@ $out
          '';

        # Expose fetch-deps so you can populate deps.nix without knowing the attribute path.
        # Usage: nix build .#fetch-deps && ./result ./deps.json
        packages.fetch-deps = self.packages.${system}.default.passthru.fetch-deps;
      }
    );
}
