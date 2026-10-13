import os, re, subprocess, tempfile

# ── CONFIGURATION ──────────────────────────────────────────────────────────────
# Set these to match how your PNG sequence was rendered.

SOURCE_FPS = 24.0   # frame rate of the source animation (Blender default is 24)
FRAME_STEP = 1      # render every Nth frame — 1 = every frame (24fps), 4 = every 4th (6fps)

# ── INPUT ──────────────────────────────────────────────────────────────────────

raw = input("Path to PNG frames folder: ").strip().strip('"').strip("'")
INPUT_DIR = raw.replace("\\", "/").rstrip("/")

if not os.path.isdir(INPUT_DIR):
    print("ERROR: Directory not found: " + INPUT_DIR)
    raise SystemExit(1)

folder_name = os.path.basename(INPUT_DIR)
OUTPUT_FILE = os.path.join(os.path.dirname(INPUT_DIR), folder_name + ".webm").replace("\\", "/")

# ── COLLECT FRAMES ─────────────────────────────────────────────────────────────

files = sorted(
    [f for f in os.listdir(INPUT_DIR) if f.lower().endswith('.png')],
    key=lambda f: int(re.sub(r'\D', '', f) or '0')
)

if not files:
    print("ERROR: No PNG files found in: " + INPUT_DIR)
    raise SystemExit(1)

PLAYBACK_FPS = SOURCE_FPS / FRAME_STEP
print("\nFound %d frames — building %.4g fps VP8 WebM with alpha ..." % (len(files), PLAYBACK_FPS))
print("Output: " + OUTPUT_FILE)

# ── BUILD FFCONCAT ─────────────────────────────────────────────────────────────
# ffconcat handles non-sequential frame numbers cleanly (e.g. Blender naming)

concat_path = os.path.join(tempfile.gettempdir(), folder_name + "_concat.txt")
duration = 1.0 / PLAYBACK_FPS

with open(concat_path, "w") as f:
    f.write("ffconcat version 1.0\n")
    for fname in files:
        fpath = os.path.join(INPUT_DIR, fname).replace("\\", "/")
        f.write("file '%s'\n" % fpath)
        f.write("duration %.6f\n" % duration)

# ── RUN FFMPEG ─────────────────────────────────────────────────────────────────

cmd = [
    "ffmpeg", "-y",
    "-f", "concat", "-safe", "0", "-i", concat_path,
    "-c:v", "libvpx",        # VP8 — supported by Unity 2018+
    "-pix_fmt", "yuva420p",  # VP8 with alpha channel
    "-auto-alt-ref", "0",    # required for alpha in VP8
    "-b:v", "1M",
    OUTPUT_FILE
]

result = subprocess.run(cmd)

if result.returncode == 0:
    print("\nDone.")
    print("WebM saved to: " + OUTPUT_FILE)
    print("Import into Unity, select it in the Project panel,")
    print("then run: Tools > chickenandribs > Video Animations > Create Quad from Video")
else:
    print("\nERROR: ffmpeg failed.")
    print("Make sure ffmpeg is installed and available in your PATH.")
    print("Download from: https://ffmpeg.org/download.html")
