# Source and license notice

GardenLayoutWork is a modified work derived from the DailyRoutines.ModulesPublic module `Interface/AutoGardensWork.cs`, authored by the Daily Routines contributors.

- Upstream repository: https://github.com/Dalamud-DailyRoutines/DailyRoutines.ModulesPublic
- Source path: https://github.com/Dalamud-DailyRoutines/DailyRoutines.ModulesPublic/blob/main/Interface/AutoGardensWork.cs
- Referenced source blob: `150a4e564f7e84be31833e6ad9365fe9b64eab89`
- Blob link: https://github.com/Dalamud-DailyRoutines/DailyRoutines.ModulesPublic/blob/150a4e564f7e84be31833e6ad9365fe9b64eab89
- License: GNU Affero General Public License, version 3. See LICENSE.

Modifications prepared 2026-11-02: rename as a separate local module; replace uniform planting with saved per-cell seed/soil layouts, configurable planting sequence, slot calibration, persistent per-house/per-garden layout bindings, maximum material preview, execution context checks and cancellation; retain upstream harvest, fertilize and tend workflows. Added independent planning model, source packaging and offline tests. This is not an official Daily Routines release.

The corresponding source is included in `source/`, with a combined local-module source file at the package root. Runtime dependencies are supplied by the user's existing Daily Routines/Dalamud installation and are not redistributed here.

2026-11-03 / 0.1.1: replace dynamically appended planting tasks with a predetermined sequential workflow to avoid a non-preempting TaskHelper queue stall; wait for addon readiness and inventory propagation; add stage-specific timeout/error notices, seed clipboard and reuse shortcuts; add behavioral regression simulations. No upstream TaskHelper code is redistributed. Workflow contract reference: https://github.com/AtmoOmen/OmenTools/blob/main/Threading/TaskHelper/TaskHelper.cs (read alongside the installed SDK implementation).

2026-11-03 / 0.2.0: add combined compact overview and garden workbench, persistent per-slot operation times, configurable per-garden reminders, bounded execution history, per-run operation guards, and allowlisted share-code export/import. The planting and maintenance workflows now track confirmations, and maintenance waits for success feedback; the original environment discovery and fertilizer inventory interaction helpers are retained. Added a standalone native ImGui renderer and further offline checks. Game messages observed in the user's trial informed the Simplified Chinese success-message matching; this package does not include the user's game configuration or character data.

2026-11-03 / 0.2.1: bound all combo popups and remove the negative fill-width input that caused continuous popup growth; add saved-house region previews, server/ward/plot address labels and persistent garden nicknames. House identity decoding follows the FFXIVClientStructs HouseId/HouseUnit layout: https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/Game/HousingManager.cs. This decodes saved values and adds no new process-memory access.

2026-11-03 / 0.2.2: support 4/6/8-cell garden furniture, variable-size editing, records and sharing; persist observed slot maps; inspect native menu disabled rows before maintenance; skip empty/ready/unavailable slots and handle verified fertilizer-denial messages without recording success; batch fertilizer reminders by the last due crop per garden. This release modifies the original discovery and fertilizer interaction helpers. Native list layout reference: https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Component/GUI/AtkComponentList.cs (validated against cached installed SDK metadata).

2026-11-03 / Omni port 0.1.0, attribution Kotiya: port GardenLayoutWork 0.2.2 to the Omni Toolbox API 1 local-module contract; use TreeTools lifecycle-owned tasks; remove Daily Routines dependencies; provide author localization (Simplified source and host-generated Traditional Chinese), scoped ordinary care-dialog progression, independent atomic state persistence with a prior-save backup, module metadata and an Omni command entry. Original authorship and AGPL licensing are retained. Omni integration references: https://github.com/YouShux/OmniToolbox.TreeHouseOnline and https://github.com/YouShux/OmniToolbox.Common . Shared runtime DLLs are not redistributed.

2026-11-04 / KotiyaGarden-Omni-0.1.1: identify known crop names from exact SystemMessage/None growth announcements; resolve crop-to-seed mapping from client GardeningSeed/Item sheets; associate a bounded observation with the current house, object, group and native menu slot; retain existing operation times and unknown planting times. Add persistent crop identity and identification time, distinct identity audit entries, and regression tests. Module descriptions use the user-selected wording. Omni visible titles omit the author suffix; Author remains Kotiya.

2026-11-05 / 1.0 attribution update: current module author/display attribution is Sanse. Existing internal module identifiers, file names and upstream license/authorship records are retained for configuration compatibility. DR declares AutoTalkSkip as a prerequisite; Omni module description asks users to enable automatic dialogue skipping.

2026-11-06 / 1.0.1, Sanse: retain discovered gardens independently of preset bindings through the existing name/slot catalog, migrate recoverable binding-only entries, provide local list deletion with confirmation and preserve layouts/history, limit quick seed candidates to four bounded slots, and center the eight-slot grid caption. Added catalog persistence/legacy/delete regressions and native ImGui interaction and geometry verification. Internal module and state identifiers remain unchanged.

2026-11-06 / 1.0.2, Sanse: add independent region/house browsing modes, persisted classification preference, identity-based house groups with full display addresses, house selection and all-house overview. Preserve original region browsing and operation scoping. Add actual native ImGui radio/dropdown interaction checks and serialization checks.


2026-11-07 / slim distribution: this installer omits development files and preview images. Corresponding source and build instructions for 1.0.2 are supplied separately at https://github.com/KKsanse/OmniToolbox.sanse/raw/refs/heads/main/downloads/KotiyaGarden-Omni-1.0.2-Sanse-source.zip . The included module binary is unchanged.
