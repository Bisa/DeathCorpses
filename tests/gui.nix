# NixOS VM test: launches Vintage Story client with the mod, navigates the GUI
# to create and start a new singleplayer world.
#
# Run:  nix build .#checks.x86_64-linux.gui
#
# The result/ directory contains numbered screenshots for debugging each step.
# Requires KVM (hardware virtualisation) on the host.

{ pkgs, lib, mod }:

let
  # vintagestory is unfree — need a pkgs instance that allows it
  pkgsUnfree = import pkgs.path {
    inherit (pkgs) system;
    config.allowUnfreePredicate = pkg: (lib.getName pkg) == "vintagestory";
  };
  # Directory containing the mod zip, suitable for --addModPath
  modDir = pkgs.runCommand "mod-dir" { } ''
    mkdir -p $out
    ln -s ${mod} $out/${mod.name}
  '';

  # Shell helper: use tesseract TSV output to locate text on screen, then click it.
  # Needs tesseract, xwd, magick, xdotool on PATH. We set TESSDATA_PREFIX explicitly.
  clickText = pkgs.writeShellScript "click-text" ''
    set -euo pipefail
    export PATH="${pkgs.lib.makeBinPath [ pkgs.tesseract pkgs.xwd pkgs.imagemagick pkgs.xdotool ]}:$PATH"
    export TESSDATA_PREFIX="${pkgs.tesseract}/share/tessdata"
    TEXT="$1"
    TIMEOUT="''${2:-30}"
    DISPLAY=''${DISPLAY:-:0}
    export DISPLAY

    deadline=$(( $(date +%s) + TIMEOUT ))
    attempt=0
    while [ "$(date +%s)" -lt "$deadline" ]; do
      attempt=$((attempt + 1))

      if ! xwd -root -silent > /tmp/_screen.xwd 2>/dev/null; then
        echo "attempt $attempt: xwd failed" >&2; sleep 2; continue
      fi
      if ! magick /tmp/_screen.xwd /tmp/_screen.png 2>/dev/null; then
        echo "attempt $attempt: magick convert failed" >&2; sleep 2; continue
      fi
      if ! tesseract /tmp/_screen.png /tmp/_ocr hocr 2>/tmp/_tess_err; then
        echo "attempt $attempt: tesseract failed: $(cat /tmp/_tess_err)" >&2; sleep 2; continue
      fi

      # Parse hOCR: extract words and their bounding boxes
      # hOCR uses double quotes: <span class="ocrx_word" ... title="bbox L T R B; ...">word</span>
      # Also handle single quotes just in case
      ${pkgs.gnused}/bin/sed -n "s/.*class=['\"]ocrx_word['\"][^>]*title=['\"]bbox \([0-9]*\) \([0-9]*\) \([0-9]*\) \([0-9]*\)[^'\"]*['\"][^>]*>\([^<]*\)<.*/\5 \1 \2 \3 \4/p" /tmp/_ocr.hocr > /tmp/_ocr_words.txt 2>/dev/null || true

      # Debug: dump raw hOCR for first 2 attempts
      if [ "$attempt" -le 2 ]; then
        echo "--- attempt $attempt: hocr file exists? $(ls -la /tmp/_ocr* 2>&1) ---" >&2
        echo "--- attempt $attempt: first 5 lines of hocr ---" >&2
        head -5 /tmp/_ocr.hocr 2>&1 >&2 || true
        echo "--- attempt $attempt: ocrx_word grep ---" >&2
        grep -c 'ocrx_word' /tmp/_ocr.hocr 2>&1 >&2 || echo "0 matches" >&2
        echo "--- attempt $attempt: any span with title bbox ---" >&2
        grep -o 'title="bbox[^"]*"[^>]*>[^<]*' /tmp/_ocr.hocr 2>/dev/null | head -10 >&2 || echo "(none)" >&2
      fi
      echo "--- attempt $attempt parsed words ---" >&2
      cat /tmp/_ocr_words.txt >&2 || true

      # Search for matching word (case-insensitive)
      match=$(awk -v pat="$TEXT" '
        tolower($1) ~ tolower(pat) {
          print $2, $3, $4, $5; exit
        }' /tmp/_ocr_words.txt 2>/dev/null || true)

      if [ -n "$match" ]; then
        read -r left top right bottom <<< "$match"
        cx=$(( (left + right) / 2 ))
        cy=$(( (top + bottom) / 2 ))
        xdotool mousemove "$cx" "$cy"
        sleep 0.2
        xdotool click 1
        echo "clicked '$TEXT' at ($cx, $cy)"
        exit 0
      fi
      sleep 2
    done

    echo "TIMEOUT: '$TEXT' not found after ''${TIMEOUT}s" >&2
    exit 1
  '';

  # Shell helper: wait until text appears on screen (OCR)
  waitForText = pkgs.writeShellScript "wait-for-text" ''
    set -euo pipefail
    TEXT="$1"
    TIMEOUT="''${2:-60}"
    DISPLAY=''${DISPLAY:-:0}
    export DISPLAY

    deadline=$(( $(date +%s) + TIMEOUT ))
    while [ "$(date +%s)" -lt "$deadline" ]; do
      xwd -root -silent | magick xwd:- png:- 2>/dev/null \
        | tesseract stdin stdout 2>/dev/null \
        | grep -qi "$TEXT" && { echo "found '$TEXT'"; exit 0; }
      sleep 1
    done

    echo "TIMEOUT: '$TEXT' not found after ''${TIMEOUT}s" >&2
    exit 1
  '';

in
pkgs.testers.nixosTest {
  name = "deathcorpses-gui";

  nodes.machine = { pkgs, config, ... }: {
    # ── VM resources ──────────────────────────────────────────────
    virtualisation = {
      memorySize = 4096;
      cores = 4;
      qemu.options = [ "-vga virtio" ];
      resolution = { x = 1280; y = 720; };
    };

    # ── Display: X11 with openbox (minimal WM) ───────────────────
    services.xserver = {
      enable = true;
      windowManager.openbox.enable = true;
    };
    services.displayManager = {
      defaultSession = "none+openbox";
      autoLogin = {
        enable = true;
        user = "test";
      };
    };

    # ── Graphics: Mesa software rendering ─────────────────────────
    hardware.graphics.enable = true;
    environment.variables.LIBGL_ALWAYS_SOFTWARE = "1";

    # ── Test user ─────────────────────────────────────────────────
    users.users.test = {
      isNormalUser = true;
      password = "test";
    };

    # ── Packages available in the VM ──────────────────────────────
    environment.systemPackages = [
      pkgsUnfree.vintagestory
      pkgs.xdotool
      pkgs.xwd
      pkgs.tesseract
      pkgs.imagemagick
    ];

    networking.firewall.enable = false;
  };

  skipLint = true;

  testScript = let
    dataPath = "/home/test/vs-test-data";
  in ''
    import time

    start_time = time.time()

    def elapsed():
        return f"[{time.time() - start_time:.0f}s]"

    def step(msg):
        machine.log(f"{elapsed()} ── {msg} ──")

    def run_as_test(cmd):
        """Run a command as the 'test' user with DISPLAY set."""
        return machine.succeed(f"su - test -c 'export DISPLAY=:0; {cmd}'")

    def click(text, timeout=30):
        step(f"clicking '{text}' (timeout={timeout}s)")
        run_as_test("${clickText} \"" + text + "\" " + str(timeout))
        step(f"clicked '{text}'")

    def wait_text(text, timeout=60):
        step(f"waiting for text '{text}' (timeout={timeout}s)")
        run_as_test("${waitForText} \"" + text + "\" " + str(timeout))
        step(f"found text '{text}'")

    def screenshot(name):
        step(f"screenshot: {name}")
        machine.screenshot(name)

    def progress_wait(seconds, label, interval=10):
        """Sleep in chunks, logging progress and taking periodic screenshots."""
        step(f"waiting {seconds}s for {label}")
        waited = 0
        i = 0
        while waited < seconds:
            chunk = min(interval, seconds - waited)
            machine.sleep(chunk)
            waited += chunk
            i += 1
            step(f"  ...{waited}/{seconds}s elapsed ({label})")
            # Check if VS is still alive during long waits
            alive = machine.execute("pgrep -f Vintagestory.dll")[0] == 0
            if not alive:
                step(f"  WARNING: Vintagestory process died during {label}!")
                screenshot(f"crashed-during-{label}")
                raise Exception(f"Vintagestory crashed during {label}")
            if waited % 20 == 0 or waited >= seconds:
                screenshot(f"progress-{label}-{waited}s")

    # ── Step 1: Boot & desktop ────────────────────────────────────
    step("STEP 1/7: Waiting for X11 display")
    machine.wait_for_x()
    step("X11 is up")
    machine.sleep(3)
    screenshot("01-desktop")

    # ── Step 2: Launch Vintage Story ──────────────────────────────
    step("STEP 2/7: Launching Vintage Story")
    step(f"  dataPath = ${dataPath}")
    step(f"  addModPath = ${modDir}")
    # Launch VS detached — redirect stdout/stderr so su doesn't block waiting for child
    machine.succeed(
        "su - test -c '"
        "export DISPLAY=:0 LIBGL_ALWAYS_SOFTWARE=1; "
        "nohup vintagestory"
        " --dataPath ${dataPath}"
        " --addModPath ${modDir}"
        " > /tmp/vs-stdout.log 2>&1 &"
        "'"
    )
    step("vintagestory process started, waiting for window")

    # Poll for the VS window — try multiple title patterns and log what we see
    deadline = time.time() + 60
    found = False
    while time.time() < deadline:
        # List all X windows for debugging
        wm_list = machine.succeed("su - test -c 'export DISPLAY=:0; xdotool search --name . getwindowname 2>/dev/null || true'").strip()
        if wm_list:
            step(f"  visible windows: {wm_list[:200]}")

        # Check if VS process is still alive
        rc, _ = machine.execute("pgrep -f Vintagestory.dll")
        if rc != 0:
            step("  WARNING: VS process not found, checking dotnet")
            ps = machine.succeed("ps aux | grep -i vintage || true")
            step(f"  processes: {ps.strip()[:200]}")

        # Try various title patterns
        for pattern in ["Vintage Story", "Vintage", "vintagestory", "OpenTK"]:
            rc, _ = machine.execute(f"su - test -c 'export DISPLAY=:0; xdotool search --name \"{pattern}\" 2>/dev/null | head -1'")
            if rc == 0:
                step(f"  found window matching '{pattern}'")
                found = True
                break
        if found:
            break

        # Take a screenshot every 10s to see what's on screen
        elapsed_secs = int(time.time() - start_time)
        if elapsed_secs % 10 < 3:
            screenshot(f"waiting-for-window-{elapsed_secs}s")

        machine.sleep(3)

    if not found:
        screenshot("no-window-found")
        # Dump VS stderr/stdout if available
        step("VS window never appeared. Checking for crash logs:")
        logs = machine.succeed("find /home/test/vs-test-data -type f 2>/dev/null | head -20 || true")
        step(f"  data dir contents: {logs.strip()}")
        crash = machine.succeed("su - test -c 'cat /home/test/.config/VintagestoryData/Logs/*.txt 2>/dev/null || true'")
        if crash.strip():
            for line in crash.strip().split("\\n")[:20]:
                step(f"  log: {line}")
        raise Exception("Vintage Story window did not appear within 60s")

    step("Vintage Story window detected")
    progress_wait(5, "main-menu-render", interval=5)
    screenshot("02-main-menu")

    # Dump OCR to log so we can see what tesseract reads
    step("OCR dump of main menu:")
    ocr = run_as_test("xwd -root -silent | magick xwd:- png:- | tesseract stdin stdout 2>/dev/null || true")
    for line in ocr.strip().split("\n"):
        if line.strip():
            step(f"  OCR: {line.strip()}")

    # ── Step 3: Dismiss login screen → Main Menu → Singleplayer ──
    step("STEP 3/8: Dismissing login screen")
    # VS shows a login screen on first launch — click "Quit Login" to go offline
    click("Quit", 15)
    machine.sleep(10)  # main menu takes time to load after dismissing login
    screenshot("03-after-login-dismiss")

    # OCR dump to verify we're at the main menu now
    step("OCR dump after login dismiss:")
    ocr = run_as_test("xwd -root -silent | magick xwd:- png:- | tesseract stdin stdout 2>/dev/null || true")
    for line in ocr.strip().split("\n"):
        if line.strip():
            step(f"  OCR: {line.strip()}")

    step("STEP 4/8: Navigating to Singleplayer")
    click("Singleplayer", 15)
    machine.sleep(3)
    screenshot("03-singleplayer")

    # ── Step 5: Singleplayer → Create new world ───────────────────
    step("STEP 5/8: Creating new world")
    click("Create", 10)
    machine.sleep(3)
    screenshot("04-create-world")

    # OCR dump of world creation screen
    step("OCR dump of world creation screen:")
    ocr = run_as_test("xwd -root -silent | magick xwd:- png:- | tesseract stdin stdout 2>/dev/null || true")
    for line in ocr.strip().split("\n"):
        if line.strip():
            step(f"  OCR: {line.strip()}")

    # ── Step 5: World creation → Start game ───────────────────────
    step("STEP 6/8: Starting game (accepting defaults)")
    click("Start", 10)
    machine.sleep(3)
    screenshot("05-starting")

    # ── Step 6: Wait for world generation & loading ───────────────
    step("STEP 7/8: Waiting for world generation (software rendering)")
    progress_wait(15, "world-gen", interval=5)
    screenshot("06-world-loaded")

    # ── Step 7: Verify ────────────────────────────────────────────
    step("STEP 8/8: Verifying")

    # Process alive?
    machine.succeed("pgrep -f Vintagestory.dll")
    step("  VS process is alive")

    # Mod loaded?
    mod_loaded = machine.succeed(
        "grep -rli 'deathcorpses' ${dataPath}/Logs/ 2>/dev/null || echo \"\""
    ).strip()
    if mod_loaded:
        step(f"  Mod found in logs: {mod_loaded}")
    else:
        step("  WARNING: 'deathcorpses' not found in logs")
        # Dump log file list for debugging
        logs = machine.succeed("find ${dataPath} -name '*.txt' -o -name '*.log' 2>/dev/null || true")
        step(f"  Available log files: {logs.strip()}")

    screenshot("07-final")
    step(f"TEST COMPLETE in {time.time() - start_time:.0f}s")
  '';
}
