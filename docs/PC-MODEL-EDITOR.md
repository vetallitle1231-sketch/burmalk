# Configurable PC model preview

This work is on `experimental/pc-model-editor`. The existing 1.1.10 installer is preserved; the model is being shown to the owner before packaging an application update.

The model is authored as WPF meshes, not a generated picture. The desktop uses a neutral tower with a removable or transparent side panel, front and top mesh, PSU shroud, motherboard, RAM, a horizontal or vertical graphics card, CPU cooling, and case fans. Desktop and laptop models remain separate. CPU cooling is selected manually because Windows cannot reliably identify an air cooler or an AIO.

The editor changes case color, side panel, board size, GPU fan count (integrated graphics or 1–3), GPU orientation, RAM module count (0–4), front/top/bottom/rear fans, air cooler fan count, radiator size, drives, and lighting. Preferences are appearance choices; they never identify sensors, write fan speeds, or change hardware settings. The same preferences are used by the main 3D page and the dashboard widget. Settings save immediately and are normalized on loading. Dashboard rotation and zoom remain persistent.

Hidden UI verification checks component selection, camera bounds, air/AIO/laptop variants, GPU editor interaction and immediate persistence, fan counts, home-widget consistency, and appearance persistence after reopening the settings. It uses an isolated data directory, leaves RTSS and hotkeys alone, and does not run any stress test.

Verification on 2026-10-14: Release preview build succeeded; `.build/verification-model-editor-final/verification.json` contains 44 captures and zero failures. The editor and main model screenshots were inspected. Both OBJ exports have valid finite coordinates, material references, indices, and consistently oriented triangle normals (14,756 air-model triangles; 20,472 AIO-model triangles). The preview ZIP passed CRC verification. No installer was rebuilt and no application update was installed during the user's game.

`Burmalk.exe --model-preview-dir <folder>` renders the same desktop meshes into three PNGs and exports two OBJ models with their MTL material files. This path starts no monitoring, overlay, hardware tuning, or game detection. The OBJ export preserves normals and transforms; it contains no user hardware identifiers.

The preview archive contains the PNGs, OBJ/MTL pairs, and viewing instructions. For viewing an OBJ in a 3D editor, keep its same-named MTL alongside it. The application itself renders its editable model directly without an external viewer or online services.
