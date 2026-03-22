{
  description = "PlayerGrave - Vintage Story mod";

  inputs = {
    nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";
    flake-utils.url = "github:numtide/flake-utils";
  };

  outputs = { self, nixpkgs, flake-utils }:
    flake-utils.lib.eachDefaultSystem (system:
      let
        pkgs = nixpkgs.legacyPackages.${system};

        modInfo = builtins.fromJSON (builtins.readFile ./modinfo.json);
        modVersion = modInfo.version;
        modId = modInfo.modid;
        vsVersion = modInfo.dependencies.game;
        vsChannel = "unstable";

        vintageStoryServer = pkgs.fetchurl {
          url = "https://cdn.vintagestory.at/gamefiles/${vsChannel}/vs_server_linux-x64_${vsVersion}.tar.gz";
          hash = "sha256-sQwwQJrfVu0B/i2fiJaHUh3Jhvl4AqyzJwPRu0WL7YA=";
        };

        vsLibs = pkgs.runCommand "vintage-story-libs-${vsVersion}" { } ''
          mkdir -p $out
          cd $out
          tar -xzf ${vintageStoryServer} --wildcards \
            'VintagestoryAPI.dll' \
            'VintagestoryLib.dll'
        '';

        commonLibZip = pkgs.fetchurl {
          url = "https://mods.vintagestory.at/download/55023/CommonLib_VS1.21.1_net8_v2.8.0.zip";
          hash = "sha256-K5LHIQom4c7TFjylxLLFv32hucyoDMwbEJBJp9fr1Kw=";
        };

        # The csproj references $(VintageStoryDataDir)/CommonLib/CommonLib.dll
        commonLibDir = pkgs.runCommand "commonlib-2.8.0" {
          buildInputs = [ pkgs.unzip ];
        } ''
          mkdir -p $out/CommonLib
          unzip ${commonLibZip} CommonLib.dll -d $out/CommonLib
        '';

      in
      {
        packages.default = pkgs.buildDotnetModule {
          pname = modId;
          version = modVersion;
          src = ./.;

          projectFile = "${modId}.csproj";
          nugetDeps = ./deps.json;

          dotnet-sdk = pkgs.dotnet-sdk_10;
          selfContained = false;

          # MSBuild reads env vars as MSBuild properties, satisfying the HintPaths
          # in ${modId}.csproj and the Exists() guards in Directory.Build.props.
          preBuild = ''
            export VintageStoryInstallDir="${vsLibs}"
            export VintageStoryDataDir="${commonLibDir}"
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
            homepage = "https://mods.vintagestory.at/show/mod/39449";
            license = pkgs.lib.licenses.mit;
          };
        };

        # Produces a .zip ready to drop into the Mods/ folder or upload to the mod portal.
        packages.zip =
          let mod = self.packages.${system}.default; in
          pkgs.runCommand "${modId}-${modVersion}.zip" { buildInputs = [ pkgs.zip ]; } ''
            cd ${mod}
            zip -r $out .
          '';

        # Expose fetch-deps so you can populate deps.nix without knowing the attribute path.
        # Usage: nix build .#fetch-deps && ./result ./deps.json
        packages.fetch-deps = self.packages.${system}.default.passthru.fetch-deps;
      }
    );
}
