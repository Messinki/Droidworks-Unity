# Droidworks Unity Importer Manifesto

## Core Philosophy
This project is building a high-fidelity importer for Sith Engine assets (Dark Forces 2: Jedi Knight) into Unity.

### 1. Reference Implementation (Blender)
- **The "Bible"**: The working Blender importer (`sith_clean/importer.py`) is the reference for file format understanding and parsing logic.
- **Different Architecture**: While we respect the Blender logic (how to read the bytes/text), our **implementation** is distinct. We use Unity's `ScriptedImporter` workflow, C# idioms, and Unity's coordinate systems.
- **Consult Before Inventing**: If you are unsure how to parse a "Thing" or a "Surface", check specifically how `importer.py` does it.

### 2. Strict Asset Integrity
- **No Fallbacks**: The asset library is curated and high quality. Missing files or failures to load are **importer bugs**, not asset issues.
- **No Placeholders**: Do not suppress errors with "Magenta Materials" or "Missing Blocks" unless absolutely necessary for debugging. Ideally, crash or error loudly so the root cause (path lookup, parsing error) can be fixed.
- **Zero Tolerance**: We are aiming for correct reproduction. If a texture is misaligned or a model is rotated wrong, it must be fixed at the math/parsing level, not tweaked manually.

### 3. Code Hygiene & Reuse
- **DRY (Don't Repeat Yourself)**:
    - **`ImporterUtils`**: This class contains the "Standard Library" for this project. Use it for:
        - `FindFile`: Robust searching in `mission`, `3do`, and `mat` folders.
        - `SithToUnity...`: Coordinate space conversions.
        - `FindAndLoadPalette`: CMP file handling.
    - **`JKLTokenizer`**: Use this for all text parsing. It handles the quirks of the JKL format (comments, quotes, separators). Do not write ad-hoc string parsing loops.
- **Coordinate Systems**:
    - **Sith**: Right-Handed, Z-Up.
    - **Unity**: Left-Handed, Y-Up.
    - **Rule**: Never manually swizzle coordinates (e.g. `new Vector3(x, z, y)`). Always use `ImporterUtils.SithToUnityPosition(v)`.

### 4. Implementation Details
- **File Structure**: Assets are located in a `mission` directory structure. Material lookups must search specific relative paths (`mat/`, `../mat/`, `3do/mat/`).
- **Instancing**: "Things" (3DOs) are instantiated as prefabs. We require strict path resolution to find the correct `.3do` asset to load.

---
**To Future Agents:**
Read this document first. If you encounter an error, assume the parser or search logic is flawed, not the asset. Reuse existing helpers.
