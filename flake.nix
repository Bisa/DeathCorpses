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

        # nixVersion: informational, used for nix store paths and zip filenames.
        # modinfo.json gets a per-target version with -vs<short> suffix (set in mkMod).
        nixVersion =
          if self ? dirtyShortRev then "${modBaseVersion}+${self.dirtyShortRev}"
          else modBaseVersion;

        # Map target info from targets.json into full build definitions.
        # The dotnet SDK is the only thing that must stay in Nix (attribute reference).
        dotnetSdks = {
          "net10.0" = pkgs.dotnet-sdk_10;
          "net8.0" = pkgs.dotnet-sdk_8;
        };

        mkTarget = name: info: info // {
          dotnetSdk = dotnetSdks.${info.targetFramework};
          vsSrc = pkgs.fetchurl {
            url = "https://cdn.vintagestory.at/gamefiles/${info.channel}/vs_server_linux-x64_${info.gameVersion}.tar.gz";
            hash = info.hash;
          };
        };

        targets = builtins.mapAttrs mkTarget targetInfo;

        mkVsLibs = target: pkgs.runCommand "vintage-story-libs-${target.gameVersion}" { } ''
          mkdir -p $out/Lib
          cd $out
          tar -xzf ${target.vsSrc} --wildcards \
            'VintagestoryAPI.dll' \
            'VintagestoryLib.dll' \
            'Lib/Newtonsoft.Json.dll' \
            'Lib/protobuf-net.dll'
        '';

        mkMod = target:
          let vsLibs = mkVsLibs target; in
          pkgs.buildDotnetModule {
            pname = "${modId}-${target.targetFramework}";
            version = nixVersion;  # nix store path only; modinfo.json is patched separately in preBuild
            src = ./.;

            projectFile = "${modId}.csproj";
            nugetDeps = ./deps/${target.targetFramework}.json;

            dotnet-sdk = target.dotnetSdk;
            selfContained = false;

            # Patch target framework early so dotnet restore sees the right TFM.
            postPatch = ''
              substituteInPlace ${modId}.csproj \
                --replace-warn '<TargetFramework>net10.0</TargetFramework>' '<TargetFramework>${target.targetFramework}</TargetFramework>'
            '';

            nativeBuildInputs = [ pkgs.jq ];

            preBuild = ''
              export VintageStoryInstallDir="${vsLibs}"
              jq '.dependencies.game = "${target.gameVersion}"' \
                modinfo.json > modinfo.tmp && mv modinfo.tmp modinfo.json
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
              description = "Vintage Story mod: persistent corpses with automatic waypoints";
              homepage = "https://mods.vintagestory.at/deathcorpses";
              license = pkgs.lib.licenses.mit;
            };
          };

        mkZip = target:
          let mod = mkMod target; in
          pkgs.runCommand "${modId}-vs${target.gameVersion}_${nixVersion}.zip" { buildInputs = [ pkgs.zip ]; } ''
            cd ${mod}
            find . | sort | zip -X -@ $out
          '';

      in
      {
        # Default targets (net10)
        packages.default = mkMod targets.net10;
        packages.zip = mkZip targets.net10;

        # Explicit per-target outputs
        packages.net10 = mkMod targets.net10;
        packages.net8 = mkMod targets.net8;
        packages.zip-net10 = mkZip targets.net10;
        packages.zip-net8 = mkZip targets.net8;

        # Expose fetch-deps for updating NuGet lockfiles.
        # Usage: nix build .#fetch-deps && ./result ./deps/net10.0.json
        #        nix build .#fetch-deps-net8 && ./result ./deps/net8.0.json
        packages.fetch-deps = self.packages.${system}.default.passthru.fetch-deps;
        packages.fetch-deps-net8 = self.packages.${system}.net8.passthru.fetch-deps;
      }
    );
}
