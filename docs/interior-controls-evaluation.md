# Interior controls evaluation

Evaluated on 17 October 2026 against repo commit `99b1bb5` and the local Big Ambitions 1.0 decompile at `~/Documents/GitHub/BigAmbitionsMCP/lib/decompiled-1.0-build-2026-09-23/`. Concurrent box-handling edits appeared during the evaluation and are outside this baseline. This is a source evaluation, not an in-game check. No game PC connection or playtest was made. Method names below were verified in that source tree, either as method bodies or explicitly identified call sites. No decompiled code is reproduced.

The current controls can place a bought bed when a suitable floor point is already known. They cannot yet support full interior decoration or reliably furnish a working business. The largest missing pieces are spatial observation, placement on specific surfaces, attachments, rearrangement and access to the designer's transactions.

## 1. How `place` works today and its weak points

### The current path

`tools/play place X Z [--rotation DEG]` passes world coordinates and an optional absolute yaw to bridge operation `place`. `TestBridgeCommands.Place` in `unity/FoodTrucks/Scripts/TestBridgeLife.cs` requires an FT city, a building where `BuildingManager.CanBuildOnCurrentBuilding` is true, no existing placement, and a paid furniture cargo entry in the closed cardboard box in the player's hands. It selects the first cargo entry. It rejects `wallMounted` items and every nonzero `stackType`.

It sets the hands panel's item and calls `UI.ItemPanel.ItemPanelUI.ClickPlace`. The game checks building height, creates the furniture controller, starts placement through `Buildings.Indoors.InteriorDesign.PlacementHelper.StartPlacementMode`, registers the instance and reduces the source cargo. The bridge sets the instance yaw and transform yaw, then casts downward at the requested X/Z from the player's current Y plus two metres, over a long vertical range.

The bridge accepts the hit if the current building grid recognizes its collider, its point passes the bridge's `OnGrid` bounds check, or its collider is a descendant of the grid component's transform. Commit `99b1bb5` added the descendant test because the home's floor tiles were part of its model rather than grid colliders. It also logs the collider, layer and height. That fixes the narrow identity assumption; this evaluation does not establish that the fix passed a subsequent game check.

For an accepted hit, the bridge reflects into `BigAmbitions.PlacementSystem.PlacementSystem.UpdateGroundItemPosition`. It reads the private `IsNewItemPlacementValid` field and public `placedInBlockedArea`. On success it copies the transform position into the instance, calls `SaveCurrentItemBeingMovedPosition`, then ends placement with `PlacementHelper.CancelPlacementMode`. On rejection it calls `CancelPlacementModeIfIsActive`, which uses the game's cargo-return or revert path. Everything runs in one main-thread call so the normal mouse-follow frame does not intervene.

The reply contains item name, `placed`, `still_placing`, a broad `why`, X/Z, requested quantized rotation and notices. It omits the instance ID, Y, floor, surface, footprint and actual final orientation. `tools/play` prints success or says the item is back in its box. That last claim follows placement-mode cleanup, not a verified source-cargo postcondition.

### Correctness risks

| Issue | What the source establishes | Consequence and needed change |
|---|---|---|
| Floor chosen from player height | A single downward ray starts at player Y plus two metres. It selects the first active ground-layer hit, without a requested floor or bounded target-height interval. | A target on another level can hit a lower floor, a surface above the intended floor, or nothing. Stairs, voids, balconies and changes in active colliders need explicit surface and floor selection. |
| Floor visibility affects hits | `MultipleHeightsBuildingController` changes upper-floor renderers and collider layers. `BuildingGridWithDifferentHeights` enables grids according to height index. | The player's position, designer view floor and requested target floor must agree. Changing only ray origin is insufficient. |
| Building membership is broad | `OnGrid` tests world bounds. The new fallback accepts descendants of the grid transform. The bridge checks the original hit before native snapping. | Bounds are not an exact floor polygon, and ancestry is not a room or floor identifier. The snapped item may cross a boundary that the original point did not. Revalidate the resolved pose and intended surface. |
| Rotation semantics differ | The bridge rounds absolute yaw to `degreesPerRotation`, with a bridge-only fallback of 45 degrees. `Math.Round` uses midpoint-to-even rounding. Native `CheckSnapRotation` advances or reverses a step and truncates its remainder. | Report the supported step and resolved yaw. Define absolute snap rounding deliberately. Support native free rotation separately. Do not treat the fallback as a verified game rule. |
| Native input can still interfere | `UpdateGroundItemPosition` calls `CheckRotation`, which reads live input. The bridge skips `UpdateItemPosition`, where `snapFreePlacement` is refreshed. Start/stop do not establish a fresh snap setting. | A prior free-placement state or concurrent input can change snapping or yaw. Own the placement update while executing and set/restore explicit mode state. |
| Partial update lifecycle | `UpdateItemPosition` normally updates instance position and calls `OnItemPositionUpdated` after movement. The bridge invokes only its ground branch and manually copies position before saving. | Some intermediate warning or coverage work is skipped. Finish callbacks still run through stop placement, so this is a lifecycle risk rather than proof of a broken save. Audit callbacks and verify the resulting instance. |
| Validation is narrower than usability | The bridge reads placement validity and blocked state, bypassing `CanPlaceItem`. The latter also checks rotation-in-progress and mouse gesture state. | Replace mouse gesture requirements with explicit command state, while retaining native geometric checks. Also report item requirements and reachability separately. |
| Cancellation is assumed successful | The game can return cargo or revert placement; cleanup can fail. No before/after cargo, registration or identity comparison is returned. | Verify conservation of furniture and contents on refusal, exceptions and cancellation. An inactive placement alone does not prove a successful rollback. |
| Diagnostics hide the cause | All ordinary invalid placements become a generic lack-of-room message. `PlacementValidation.CurrentOverlappingCollider` is available but unused. | Return the observed blocker, target surface, resolved pose and separate validation results. Mark unknown causes as unknown. |

Ceiling furniture is an additional scope mismatch. The bridge excludes walls and stacks but does not explicitly exclude `snapToCeiling`. Native ground placement can call `TryPlaceItemOnRoof`, so the command described as floor-only can enter the ceiling path. It has no ceiling observation or result fields to explain that outcome.

### What the agent can see today

`look` gives position, inside address, held contents and rented homes, alongside player and fleet state. `use list` exposes at most forty nearby clickable entities, filters by approximate height and omits walls and noninteractive decoration. Its raw entries include X/Z, but `tools/play use` prints distance and labels without those coordinates. Its list IDs are temporary selections, not persistent furniture identities.

`use click` can buy from a shelf, use a till, enter a building or interact with a bed. It supports only a left click and explicitly cancels placement if a click starts it. It therefore cannot rearrange ordinary furniture. `walk` can navigate inside and leave through an exit, but supplies no room survey. `screen` returns the game's frame when available and is useful visual evidence; its fallback can be the separately rendered shot. It provides no metric floor map or selectable wall faces.

`TestBridgeUi` can invoke buttons, dropdown options, toggles, sliders and Escape. `ui set` only sets sliders. It cannot type a furniture search or blueprint name, target a world surface, drag a queue or place an object. Its visibility test also does not fully account for clipping or occluding windows. A designer button being clickable does not make the designer controllable.

`TestBridgeFixture` can create warehouse furnishing and stock for logistics tests. Those are fixture mutations, not player controls, and are unavailable through `tools/play`. Fleet equipment transfers and commissary orders do not cover ordinary building furniture. There is no dedicated interior map, placed-item inventory, footprint, wall, attachment, material, room-requirement or designer command.

A separate session is adding put-down/pick-up of held boxes and hand truck release. Those are dependencies for the acquisition loop. This report does not design those controls or assume they are already available.

## 2. The game's full set of interior actions

### Verified action paths

Source paths in this section are relative to the decompile root. Classes without a namespace, including `ItemController`, `ItemHelper`, `BuildingManager` and `MultipleHeightsBuildingController`, are in `BigAmbitions/`. Designer setup classes are in `BigAmbitions/Buildings.Indoors.InteriorDesign/`. Placement classes are in `BigAmbitions.PlacementSystem/BigAmbitions.PlacementSystem/`. This identifies local evidence without placing game source in this repo.

Some core designer tool implementations are absent from the supplied decompile. For example, it contains references to `HandTool`, `InteriorElementTool` and reversible-action types, but not their class bodies. The table names verified setup methods and explicit call sites in those cases. Their deeper input handling and rollback implementations remain unverified.

| Player action | Verified class and method path | Important behavior |
|---|---|---|
| Open or close the interior designer | `UI.InteriorDesigner.InteriorDesignerUI.Show`, `CanOpen`, `HideWithConfirm`, `Hide`; `InteriorDesignerController.OnOpen`, `HandleOnClose` | Native editing maintains a staged balance and undo history. Close applies the transaction and final item-change processing. Keep this lifecycle intact. |
| Apply, discard, undo or redo edits | `InteriorDesignerUI.ExecuteRevertibleAction`, `Undo`, `Redo`, `HideWithConfirm`, `UndoAllAndHideWithConfirm` | Apply and discard use different paths. Discard undoes the action stack and serializes surfaces. Escape is context dependent. |
| Place furniture from a held box | `UI.ItemPanel.ItemPanelUI.ClickPlace`, `TryToStartPlacingItem`; `PlacementHelper.StartPlacementMode` | Uses cargo conversion, native height checks and building registration, rather than granting an item. |
| Position ground furniture | `PlacementSystem.UpdateItemPosition`, `UpdateGroundItemPosition`, `UpdatePositionOfCurrentItem` | Uses `groundLayerMask`, blocked surfaces, per-item `gridSize`, `snapFreePlacement` and collision validation. |
| Position wall-mounted furniture | `PlacementSystem.UpdateWallItemPosition`; `PlacementValidation.IsWallPlacementValid`, `IsThereAWall` | Raycasts with `wallPlacementLayerMask`, aligns to wall normal, snaps on the wall plane and checks backing at the item and collider corners. A wall face and height are required. |
| Stack or attach furniture | `PlacementSystem.UpdateStackItemPosition`, `SetItemPosition`; `ItemController.GetAvailableAttachmentPoints`, `SetToParentPlaceableItem`, `RemoveFromParentPlaceableItem` | Uses `guidersLayerMask`, `AttachmentPointType`, parent-relative snapping, attachment rotation and parent/child persistence. If no attachment is hit, the stack branch can fall back to ground placement. |
| Attach seats, computers, work surfaces or factory parts | `BigAmbitions.Items.AttachmentPoint.Toggle`; properties on `SeatAttachmentPoint`, `ComputerAttachmentPoint`, `WorkSurfaceAttachmentPoint`, `FactoryMachineAttachmentPoint`; `PlacementSystem.UpdateStackItemPosition` | Attachment types have different position and rotation constraints. Factory alignment can use alignment transforms. Expose the actual target slot and allowed behavior. |
| Place ceiling-mounted furniture | `PlacementSystem.TryPlaceItemOnRoof`, `GroundGridAllowsRoofPlacement`, `GetCeilingYPositionForRoofObject`; `MultipleHeightsBuildingController.FitsInPosition`, `GetCeilingYPositionForRoofObject` | This is ceiling snapping from a ground target, not unrestricted placement on an exterior roof. Grid permission and local ceiling height matter. |
| Rotate in steps, reverse rotation or rotate freely | `PlacementSystem.CheckRotation`, `CheckSnapRotation`, `CheckFreeRotation` | Native input distinguishes step rotation, inverse step and free yaw. Attachment slots can enforce their own rotation. |
| Finish, cancel or revert placement | `PlacementSystem.CanPlaceItem`, `SaveCurrentItemBeingMovedPosition`, `SetItemPosition`, `RevertPlacement`, `StopPlacingItem`; `PlacementHelper.Run`, `CancelPlacementModeIfIsActive` | Saving a pose alone does not perform all cleanup. Cancellation distinguishes a new cargo item from an existing object. |
| Move an existing object or attached group | `ItemController.SecondaryInteract`; `PlacementHelper.StartPlacementMode`; `InteriorDesignerController.StartMovingItem`; `HandToolSetup.TryToStartItemPlacement` | Native movement checks structure restrictions and occupancy. Designer movement uses `HandTool.SelectItem` at a verified call site. Children and attachment relationships must survive movement and undo. |
| Pick up placed furniture into cargo | `UI.ItemPanel.ItemPanelUI.ClickGrab`; `ItemController.TryToGrabItem` | Rejects attached children and stocked storage shelves. Converts furniture to cargo and removes registration, shifts and producer references. This is distinct from picking up a loose held-box object. |
| Pack furniture into a movable box | `PackageToolSetup.CanBePacked`, `PackItem` | Designer packing has stock, child, occupancy and grab restrictions and creates a box through the native reversible action flow. It is not a general item-storage database. |
| Transfer or discard stored cargo | `PackageToolSetup.OpenPackageOverlay`, `MoveCargo`, `MoveCargoToStorage`, `DiscardCargo`, `DiscardSingleCargo`; `FridgeController.TryMoveCargoToStorage` | Preserves capacities and item restrictions. Nested cargo, custom colours and partial transfers require explicit accounting. |
| Sell furniture, including attached items | `UI.ItemPanel.ItemPanelUI.ClickSell`; `SellToolSetup.Setup`, `GetEnabledItemControllerIndicesInItem`, `GetSellPrice`, `GetIsOccupied`, `HideItems`, `AddToSoldList`; `InteriorDesignerController.HandleOnClose` | Ordinary panel sale and staged designer sale are separate flows. `HandToolSetup.Setup` explicitly references `SellTool.SellItem`. Designer close finalizes removal. |
| Discard furniture or cargo | `UI.ItemPanel.ItemPanelUI.ClickDiscard`, `DiscardPlacementSelectedItem`; `PackageToolSetup.DiscardCargo`, `DiscardSingleCargo` | Discard is distinct from a sale. The placement-panel discard refuses attached children and preserves native confirmation and special-gift behavior. The recursive `DiscardItemWithAttachedItems` helper is used by the sale path, not a license to bypass discard restrictions. |
| Mark a furnished item for sale | `SellToolSetup.ToggleItemForSale` | Toggles purchaser settings and sale visuals. This is distinct from immediately selling it back. Availability in normal play versus editor mode needs checking. |
| Add furniture directly through the designer | `UI.InteriorDesigner.FurnitureActionPanelUi.OnOpen`, `SelectButton`; `FurnitureToolSetup.PlaceItemFurnitureTool`; `InteriorDesignerUI.ExecuteRevertibleAction` | Native catalogue selection creates a temporary object and a `FurnitureRevertibleAction` at a verified constructor call site. Normal play and blueprint mode have different economic contexts. |
| Duplicate one item or its attached group | `DuplicateToolSetup.DuplicateItem`, `CreateDuplicate`, `DuplicateProperties` | Duplicates use new instances, carry colour, text and stock-choice properties, optionally include children and stage their cost through the designer action. |
| Change furniture colours | `PaletteToolSetup.OpenColorOverlay`, `ApplyColors`, `UndoColors`; `BigAmbitions.InteriorDesigner.ColorOverlay.Open`, `ApplyColors`, `UndoColors` | Only supported custom colour channels are editable. The setup connects `PaletteTool.OnColorChosen` at a verified call site. |
| Sample and reuse furniture colours | `EyedropperToolSetup.Setup`, `GetSelectedColor`, `GetSupportedChannels`, `GetOriginalColor`, `OnPasteModeChange` | Native eyedropper support is distinct from choosing a palette colour. Its tool body is absent from this snapshot. |
| Change wall or floor finish | `FloorToolSetup.Setup`, `WallToolSetup.Setup`; `InteriorDesignerController.GetInteriorElementClicked`, `TryGetInteriorElementClicked`; `UI.InteriorDesigner.InteriorElementActionPanelUI.SelectButtonByPresetId`, `OnInteriorElementsPresetsChanged`; `BuildingManager.SerializeInteriorDesign` | Selects an `InteriorElement` UUID and material/submesh index. Offers purchasable wall/floor presets and colour variants. The actual paint tool body is absent, so no invented paint-apply method is claimed here. |
| Choose finish presets and colours | `InteriorElementActionPanelUI.SetMaterialPresetsList`, `SelectInteriorElementColor` | Presets are filtered by wall/floor material type and purchase eligibility. No separate wallpaper action or wallpaper-specific class was found. Wallpaper-like finishes, if present in assets, belong in this preset catalogue. |
| Configure a shelf's product or producer | `ProducerToolSetup.SetStockOption`; `ItemController.OnStockOptionSelected` | Empty furniture is not necessarily configured for the business's product. The designer uses reversible actions wired by `ProducerToolSetup.Setup`. |
| Set sign text or factory configuration | `ProducerToolSetup.SetWorldSpaceText`, `SetFactoryMachineOption`; `Controllers.ItemWithTextController.SetText` | Producer tooling also handles text/sign items and assembly-machine recipe/workstation choices. Expose only choices available for the selected object. |
| Edit a customer queue | `QueueToolSetup.OpenQueueOverlay`, `PerformQueueClick`, `SetAnchors`; `EmployeeStations.WaitingLine.LeftClick`, `RightClick`, `SetCustomAnchors` at verified call sites | Queue anchors belong to a specific waiting-line holder. They are not furniture footprints. `QueueTool.OnQueueChanged` is connected at a verified call site. |
| Inspect security coverage and move cameras | `SecurityToolSetup.GetSecurityItems`, `GetSelectedCameraIndex`, `Setup`; `InteriorDesignerController.SetPositionRotationAndAttachment`; `BusinessSecurityHelper.UpdateCamerasCoverage` | Coverage needs inspection after placement. The security tool delegates camera movement to the hand tool. |
| Change viewed floor, wall visibility or lighting preview | `InteriorDesignerUI.ShowUpperFloor`, `ShowLowerFloor`, `ChangeFloor`, `ToggleWalls`; `BigAmbitions.InteriorDesigner.Tools.ChangeFloorRevertibleAction.Do`, `Undo`; `TimeOfDayToolSetup.Setup` | Floor viewing updates native height/grid/camera state. The extra top-view index in `ChangeFloorRevertibleAction.ApplyViewFloor` is a camera view, not another physical floor. Time-of-day tooling calls `TimeOfDayController.SetEnvironmentSettings` and `UpdateHourlyValues` at verified call sites; it is an environment preview. |
| Save a local blueprint | `UI.InteriorDesigner.SaveBlueprintUI.OnSaveLayoutAsBlueprint`, `Save`, `SaveLayoutInternal`, `SaveLocalBlueprint`; `BusinessLayoutSets.BusinessLayoutSetHelper.Collect` | Captures layout, finishes, building size/version, mod dependencies, metadata and thumbnails. File completion is asynchronous. |
| Create or edit a blueprint outside the city | `BigAmbitions.BlueprintCreator.BlueprintCreatorSystem.InitAsync`, `StartEditing`, `ConfigureBlueprintBuilding`, `SetupPlacementSystem`, `ApplyLayout`, `OpenInteriorDesigner`, `SaveAndOpenBlueprint` | Uses a separate scene and editing context. It shares placement/design systems but disables tools including the package tool. Current FT-city command guards cannot simply be reused. |
| Preview or order blueprint installation in play | `Buildings.InteriorInstallationFirmHelper.GetBlueprintNames`; `Dialogs.InteriorInstallationFirmAgentDialog.OnDesignSettingsSet`, `SetDesignContract`; `Entities.InteriorInstallationFirmContract.DoInstallation`, `Install` | Native installation schedules work, charges through the game and replaces existing contents, accounting for sale value. The player must be outside the target when installation runs. |

`Blueprints.BlueprintsLayoutHelper.LoadBlueprint` is a verified console helper that clears and inserts a layout directly. It is not the paid installation route and should not be exposed as ordinary gameplay. The wall tool verified here paints existing elements. No structural wall-building, door-cutting or freely editable room topology action was established by these sources.

### Buying furniture and getting it indoors

`Controllers.PlayerItemPurchaser.GrabItem` checks store stock, customer type, shopping-container requirements and available cargo space. It can add unpaid cargo to a carried container or the vehicle being pushed, or create a box in hands. Furniture colour choices can be copied from the shop display. `Controllers.CashRegisterController.Order` opens `UI.Purchase.PurchaseUI`; `PlaceOrder` hands the order to its native callback. `SetCargoInstancesToPaid` is part of the purchase flow. Starting an order is not proof that payment and handover have finished.

The bridge already reaches shelves, tills and contacts through `use`, `phone`, `call` and `ui`. A complete furnishing loop also needs selectable cargo in hands, hand trucks, delivery spots, vehicles and storage. `ItemPanelUI.ClickPlace` only selecting the first furniture entry is insufficient for a mixed delivery. General car/van cargo handling is also outside the fleet-specific `vehicle` commands. Coordinate these dependencies with the separate box-handling session rather than duplicate its work.

Remote furniture ordering is verified through `Dialogs.FurnitureStoreManagerDialog.FurnitureDeliveryContract`, `OnDeliveryContractSettingsSet` and `OnCancelFurnitureDelivery`. `UI.Dialog.FurnitureDeliveryContractSettings.GetDeliveryDestinations`, `GenerateDeliverySlots` and `GetItemsForSale` provide eligible destinations, slots and goods. `Services.ContractItemsForSaleService.TryGetItemsForContact` allows a contact catalogue without a physical shop registration. This supports remote/contact ordering; a separate web or online-checkout UI was not located in the supplied source and should not be invented.

`Buildings.BuildingTypes.Special.FurnitureStore.FurnitureDeliveryHelper.RunHourly` processes due contracts, `DeliverFurnitureContract` attempts payment, and `UpdateDeliverySpot` adds cargo to the building's delivery spot. `GetDeliverySpotInstance` can create that spot. Delivery is cargo arrival, not installation. Later controls need to show scheduled, delivered and failed states, then let the player select and place what arrived. Use actual native order options and notices, without publishing delivery limits or price formulas in the agent manual.

### What makes an interior usable

`PlacementValidation.IsGroundPlacementValid` checks supported item colliders and children against the placement collision mask. The mask includes walls, interactive items, people, vehicles and unplaceable ground. Wall validation checks backing and overlap. Blocked `InteriorElement` surfaces are a separate rejection, with a ceiling-specific exception. Flat decoration has a special placement path and must not be treated as an ordinary solid obstacle by a bridge map.

These checks are not a proof of a working room. `ItemController.GetNavMeshTargetPosition`, `GetNavMeshTargetPositionClosestToTheItem` and `EmployeeStationController.GetEmployeePosition` identify interaction/work positions. `OnItemPlacementFinished` schedules navigation-target updates after placement. Report reachability only after those updates, distinguish player access from employee/customer access, and preserve exit and queue routes. Any bridge-generated access overlay is advisory unless confirmed by the relevant native path or interaction.

Business requirements come from `BusinessType.businessRequirements`, obtained through `Helpers.BusinessTypeHelper.GetData`. `UI.InteriorDesigner.BusinessRequirements.BusinessRequirementsInfoPanelUI.UpdateBusinessRequirements` evaluates each requirement and displays its localized name and help link. `Helpers.BusinessHelper.AreAllRequirementsMet` and `AreWorkCriticalRequirementsMet` establish overall status. This must be queried for the selected business type and building, including modded types.

Verified requirement implementations include `SpecificItemsInBuilding.IsRequirementMet`, `ItemsOfTypeInBuilding.IsRequirementMet`, their area-dependent variants, `SpecificItemsInShelves.IsRequirementMet`, `AnyPrimaryProduct.IsRequirementMet` and `PaidLicensingFees.IsRequirementMet`. Specific-items presence can accept an alternative item, whereas shelf-product requirements check available products. `AnyPrimaryProduct.GetRequiredItemsForTutorial` finds supporting showcase furniture; its attachment helper explicitly accounts for a counter. A universal list of a till, shelf and counter would miss these differences.

At item level, `ItemHelper.GetMissingRequirements` and `HasAnyMissingRequirements` evaluate `Item.furnitureRequirements`. Verified checks include `Furniture.Requirements.HasItemTypeAttached.IsRequirementMet`, `IsPlacedAtHeight.IsRequirementMet`, `IsPlacedInTheEntrance.IsRequirementMet`, `IsPlacedInBusinessOfType.IsRequirementMet` and `IsPlacedInBusinessOfTypes.IsRequirementMet`. `ItemController.GetWarningIconType` exposes missing requirements, and `EmployeeStationController` checks them before staffing a station. Merely putting a register beside a counter does not establish its attachment requirement.

The following are coverage groups for control design, not verified fixed shopping lists. Exact mandatory furniture per business type is serialized in assets, not enumerated by these C# definitions. The runtime checklist must resolve that remaining evidence gap.

| Interior/business group | What the controls must cover | How to establish the actual needs |
|---|---|---|
| Gift, clothing, jewellery, liquor, florist, fruit/vegetable, electronics, book and other retail shops | Product-bearing shelves/displays, checkout and its attachments, stock storage, customer access and any shopping-container facilities | Read that type's requirement assets, shelf/product compatibility and item warnings. Do not assume the same display or checkout requirements across types. |
| Coffee shops and fast food restaurants | Product-producing equipment, counters/work surfaces, checkout, queues and any seating or facilities required by that type | Query primary-product support and attachment requirements, then stock and access. Food equipment on the floor can be geometrically valid but functionally incomplete. |
| Web development, law, marketing, graphic design, travel and event-planning offices; headquarters | Workstations, desks/computers/seats as applicable, employee access and support furniture | Query each loaded type and workstation's requirements. Headquarters is a separate business context, not a renamed retail shop. |
| Hairdresser, gym, clinic, nightclub, casino, cinema and theatre | Specialized service/production stations, queue or waiting positions, seating and applicable facilities/security | Query native requirements and per-item configuration. Cover their actual available stock/recipe/text options rather than force the retail model. |
| Warehouse and factory | Storage, workstations/machines, attachment chains, cargo transfers, working access and delivery areas | Query the type plus item requirements and attachment points. Warehouse vehicles and factory configuration have additional operational controls beyond decoration. |
| Special services and other loaded or modded types | The same geometry, item and requirement inspection where the player can furnish them | Enumerate `BusinessTypeHelper.GetAllPlayerAvailableBusinesses` for the current building type. A legacy enum map is not proof of current player availability. |
| Homes | Bed, storage/fridge, seating, television, computer and hygiene furniture where available, plus lighting, decoration and finishes | Inspect the actual items and native interactions. `BedController.PerformActivity`, `TVController.PerformActivity`, `FridgeController.ConsumeItem`/`TryAddToStorage` and `HygieneItemController.PerformActivity` are verified paths. A bed supplies an activity; this does not imply every listed object is mandatory for a home. |

## 3. Gap table

Effort: S is a small query or wrapper; M needs a new adapter and coordinated client/output changes; L spans native editing state, geometry or several workflows. These are relative sizes, not time promises. All new game adapters remain unverified until a later game check.

| Action | Game path | Bridge today | What to build | Effort | Risk |
|---|---|---|---|---|---|
| Inspect room and floors | `BuildingGridBase.GetGroundGrids`; `MultipleHeightsBuildingController.GetFloorsParents`, `GetItemHeightIndex`; building interior elements | Inside address and nearby clicks | Current-building survey with floor IDs, surfaces, walls, openings, exits and coordinate frame | L | Bounds or inactive colliders can misdescribe geometry |
| Inspect placed items and footprints | `ItemController.GetColliders`, `GetGroundIndicators`, `GetAttachmentPoints` | Partial clickable list; no footprints | All current-building items, IDs, oriented collider footprints, height spans and relationships | M | Visual size, collision size and access area differ |
| Find free areas for an item | `PlacementValidation.IsGroundPlacementValid`, `IsWallPlacementValid`; native placement branch | Trial and generic failure | Sampled candidates using selected item's actual native validation, with blockers and coverage limits | L | A free map cell is not a placement guarantee |
| Place floor furniture reliably | `ItemPanelUI.ClickPlace`; `PlacementSystem.UpdateGroundItemPosition` | Single world X/Z attempt | Explicit surface/floor, snap mode, final pose, callbacks and rollback postconditions | M | Wrong floor or incomplete lifecycle |
| Preview and cancel | `PlacementHelper.StartPlacementMode`, `CancelPlacementModeIfIsActive`; `PlacementSystem.RevertPlacement` | Automatic cancellation after failure | Controlled candidate evaluation with native rollback and no mouse-frame interference | L | Cargo loss, duplication or stale global state |
| Rotate or disable snapping | `CheckSnapRotation`, `CheckFreeRotation`; `snapFreePlacement`, item `gridSize` | Quantized yaw only; implicit snap state | Absolute snap/free yaw and explicit grid/free placement, reporting resolved behavior | M | Input interference and attachment overrides |
| Place wall items | `UpdateWallItemPosition`; `IsWallPlacementValid` | Refused | Wall-face handle, wall-plane coordinates, mounting height and normal | M | Wrong face or incomplete backing |
| Stack and attach | `UpdateStackItemPosition`; `SetItemPosition` | Refused | Parent/slot handles, compatible attachment discovery and constrained pose | L | Lost parent links or incompatible/occupied slot |
| Place ceiling items | `TryPlaceItemOnRoof`; `GroundGridAllowsRoofPlacement` | Incidental ground path, no dedicated contract | Ceiling target mode and floor-specific clearance/results | M | Wrong ceiling height or forbidden grid |
| Move placed furniture | `SecondaryInteract`; `StartPlacementMode`; `HandTool.SelectItem` call site | No right click; placement cancelled by `use` | Move-by-instance using the shared placement adapter and native revert | M | Occupancy, children, shifts and rollback |
| Pick up placed furniture | `ItemPanelUI.ClickGrab`; `ItemController.TryToGrabItem` | No dedicated control | Native furniture-to-cargo action and destination/result inspection | M | Stock and attachment restrictions; coordinate box work |
| Pack and store furniture | `PackageToolSetup.CanBePacked`, `PackItem`, `MoveCargo` | No furniture package API | Designer pack and existing-holder transfers, with content accounting | L | Nested cargo, partial transfer and undo |
| Sell or mark for sale | `ClickSell`; `SellToolSetup`; `HandleOnClose` | Some panel buttons may be reachable | Targeted sale quote/action, affected children, native confirmation and staged/final status | M | Permanent removal after apply; sale modes differ |
| Discard furniture or cargo | `ItemPanelUI.ClickDiscard`, `DiscardPlacementSelectedItem`; package discard methods | Some panel buttons may be reachable | Targeted discard with affected items/contents and native confirmation | M | Permanent loss; native discard refuses attached children |
| Open/apply/discard/undo/redo designer | `InteriorDesignerUI`; `InteriorDesignerController.HandleOnClose` | Generic UI only | Explicit designer session/state commands through native action stack | M | Incorrect settlement or confused Escape behavior |
| Buy from designer catalogue | `FurnitureActionPanelUi.SelectButton`; `FurnitureToolSetup.PlaceItemFurnitureTool` | Buttons partly accessible; no world target | Filtered native catalogue, staged selection/placement and actual displayed cost | L | Unpaid creation if action/close lifecycle is bypassed |
| Duplicate furniture | `DuplicateToolSetup.DuplicateItem`, `CreateDuplicate` | None | Native duplicate, single/group choice and normal placement | M | Children, configuration and staged charge |
| Recolour or sample colour | `PaletteToolSetup`; `ColorOverlay`; `EyedropperToolSetup` | Some colour buttons only | Supported channels, colour selection, sampling/reset through native actions | M | Unsupported channels or lost undo state |
| Paint walls/floors and select finishes | `WallToolSetup`, `FloorToolSetup`; `InteriorElementActionPanelUI`; `SerializeInteriorDesign` | Preset buttons alone cannot paint a target | Surface/submesh selection, native material/variant catalogue and reversible application | L | Missing tool bodies; incorrect face or serialization |
| Configure products, signs and machines | `ProducerToolSetup.SetStockOption`, `SetWorldSpaceText`, `SetFactoryMachineOption` | Generic UI options; no stable item target or text input | Per-item choices and native reversible setters | M | Configuration may change available producers/stock |
| Edit queues | `QueueToolSetup.SetAnchors`; `WaitingLine.SetCustomAnchors` call site | None | Ordered anchors with native validation and undo | M | Overlap, inaccessible queues or wrong owner |
| Check business and item requirements | `BusinessRequirementsInfoPanelUI`; `BusinessHelper`; `ItemHelper.GetMissingRequirements` | No structured interior checklist | Localized native status and help for current building/items | M | Asset-dependent requirements; avoid guessed prescriptions |
| Inspect security and access | `BusinessSecurityHelper.UpdateCamerasCoverage`; navigation/work targets | No interior coverage/access report | Player-visible coverage, item warnings and separate access checks | M | Stale navigation data or overclaiming usability |
| Shop, deliver and unpack chosen cargo | `PlayerItemPurchaser`; `PurchaseUI`; furniture contracts and delivery helper | Shelf/till/contact/UI basics; first held cargo only | Complete order fields/status and cargo-source selection, integrating separate box controls | L | Payment/arrival is asynchronous; mixed cargo and capacity |
| Type into native UI | `FurnitureActionPanelUi` search; `SaveBlueprintUI` name flow | `ui set` only supports sliders | Guarded text-input listing/setting with native change/end-edit behavior | M | Wrong field or stale UI identity |
| View floors, walls and lighting | `ChangeFloor`, `ToggleWalls`; `TimeOfDayToolSetup` | Some buttons may work | Explicit inspection/view controls and selected-floor reporting | S | View change can change raycast targets |
| Save and install a blueprint in play | `SaveBlueprintUI`; `InteriorInstallationFirmAgentDialog`; installation contract | No dedicated controls; no name entry | Local save completion, compatible library/preview and paid installation order/status | L | Whole-interior replacement and delayed installation |
| Use standalone blueprint creator | `BlueprintCreatorSystem` | Acting operations require FT city | Separate editor context, scene guards and reuse of interior adapters | L | No active city/save; tools and economics differ |

## 4. A proposed control set for tools/play

### Common contract

Keep `place X Z [--rotation DEG]` as the simple held-box command. Extend it through the same placement adapter used for moving and designer objects. New interior commands operate on the building the player is currently allowed to inspect or edit. They must not reveal other buildings' saved layouts, grant furniture, bypass payment or expose console/fixture operations.

Give each survey an address, bridge session, building-load generation and layout revision. Item handles resolve to actual instance IDs; wall/surface handles resolve to element UUID plus face/submesh and floor; attachment handles resolve to parent plus slot. Refuse handles from another session/building or changed target. After a load, installation, sale or other layout change, refresh affected observations. Do not expose native list indexes as durable IDs.

Use world X/Y/Z for compatibility. A room map may also supply a documented building-local frame with origin, yaw and conversion to world coordinates. Commands must state which frame they accept. Yaw is clockwise from +Z in the world frame; wall coordinates need a labelled origin, tangent, inward normal and height reference. Floor selection must select a real native level and surface, rather than merely replacing player Y in the current ray.

Every mutation reports `done`, affected item/source/parent IDs, requested and resolved pose, floor/surface, notices, resulting revision and whether placement or designer changes remain active. Report actual final yaw, not just the rounded request. Failures distinguish wrong floor/surface, blocked area, overlap, missing wall backing, incompatible attachment, height restriction, occupancy, missing requirements and unknown native refusal. Include a visible blocker name/handle where available. A failed native check must stay failed even if an advisory map thinks the location is free.

Reuse the bridge's operation IDs and session checks. Poll an existing operation after timeout; do not automatically repeat a purchase, placement, sale or install. Keep the `tools/play` STOP-file and save/character guards. Editor support needs a separate deliberate context rather than weakening the city guard globally.

### Seeing a room

| Proposed command | Output |
|---|---|
| `interior look` | Current address, ownership/editability, building/business type, selected/viewed floor, available floors, coordinate frame, held furniture, active placement/designer state and native warnings |
| `interior map [--floor FLOOR] [--cell METRES]` | Compact text floor map plus optional structured representation: floor boundary and holes, wall faces, doors/openings/stairs/exits, fixed structure, placed-item footprint labels and player position; legend, scale, bounds, revision and unknown areas |
| `interior items [--floor FLOOR] [--category CATEGORY]` | All observed placed items on that floor, including nonclickable decoration, with handle, name, pose, footprint bounds, height span, parent/children, current use, move/pack eligibility and warnings |
| `interior inspect ITEM` | Detailed oriented footprint polygons/collider parts, interaction and employee positions, attachment slots and their constraints, supported colours, stock/text/recipe options, contents and native missing requirements |
| `interior surfaces [--floor FLOOR]` | Floor and wall handles, element/face identity, geometry, height range, inward normal, current finish, paintability, blocked placement state and ceiling-placement permission where known |
| `interior requirements` | The current business type's own localized checklist and item warnings, fulfilled/missing status, help links and relevant observed items; distinguish operational requirements from optional decoration |
| `interior spots --source SOURCE [--floor FLOOR] [--rotation DEG] [--limit N]` | Bounded candidate poses for the selected item, resolved footprint, native validity, observed blockers and access status; sampled region/resolution and truncation flags |
| `interior access [--item ITEM]` | Reachable, blocked or unknown player interaction/work/exit routes, tested agent context and data freshness; separate native item requirement failures |
| `interior security` | The coverage and warnings the native security view offers, associated camera/panel handles and freshness |

The map must combine actual floor/model geometry with placement grids. The home-floor bug shows why grids alone are insufficient. Preserve concave outlines, holes and separate levels. An axis-aligned bounds rectangle can be a summary, never the claim that all enclosed space is floor. If exact geometry is unavailable, return sampled geometry with an uncertainty label.

Show physical collision footprints separately from access positions and queues. A chair's interaction area, a register's staff side and a queue are not interchangeable with its visible mesh. Retain vertical spans so a wall object, ceiling light, rug and table can share a plan coordinate where native rules allow it. Show parent/child groups without counting their footprint as independent solid obstacles against themselves. Mark fixed building structure so the agent does not repeatedly try to move it.

Free areas are item-specific. `spots` should validate a temporary selected item through the correct native branch, then restore the original state through native cancellation. It must preserve cargo, money, registrations, relationships and navigation/placement flags. This is internal temporary mutation, even though the user-facing result is a query. Until rollback is proven, ship observation-only maps and footprints before `spots`. Bound work per call, and do not claim a sampled search found every free pose. A geometric map can be cached by revision; moving people and freshly changed access require validation again at commit.

Example readable output, with geometry only:

```text
Interior: <current address>, floor F1, revision R7. Coordinates: world metres.
Map: north is +Z. # wall, . floor, X fixed/blocked, A item, E exit, ? unknown.
Item A: <bed handle>, floor F1, yaw <actual angle>, footprint <polygon>.
Access: bed interaction reachable; exit reachable; employee access not checked.
Candidate C1: <pose>, valid now; snaps to <resolved pose>; no observed blocker.
```

### Placing, moving and editing

| Proposed command | Output and native behavior |
|---|---|
| `interior cargo [--source hands\|HOLDER]` | Selectable cargo entries, paid state and container location, coordinating holder semantics with the separate box session |
| `place X Z [--floor FLOOR] [--surface SURFACE] [--rotation DEG] [--snap grid\|free] [--rotate snap\|free] [--source SOURCE]` | Ground/ceiling result with actual pose and source-cargo change. Keep the existing default source as held furniture. Refuse ambiguous floors. |
| `place wall WALL --along METRES --height METRES [--source SOURCE] [--snap grid\|free]` | Mounted item, face/normal, resolved position, backing/overlap status and any orientation constraint |
| `place on PARENT --slot SLOT [--along U --across V] [--rotation DEG] [--source SOURCE]` | Compatible attachment, parent/child identities and resolved pose; reject offsets/rotation where the native slot fixes them |
| `place ceiling X Z --floor FLOOR [--source SOURCE] [--rotation DEG]` | Selected supporting floor, native ceiling height/permission and resulting pose |
| `interior preview --source SOURCE --at X Z [placement options]` | Resolved pose, footprint, validity, blockers and requirements, followed by verified rollback; equivalent wall/attachment/ceiling target forms |
| `interior move ITEM --at X Z [placement options]` | Validated move of the same instance/group, before/after pose and relationships; equivalent wall/attachment target forms |
| `interior rotate ITEM DEG [--mode snap\|free]` | A validated move at its current position, reporting attachment-imposed restrictions and actual yaw |
| `interior cancel` | Native cancellation result, restored item/cargo/pose and whether a placement remains active |
| `interior grab ITEM` | Native placed-furniture pickup result and resulting cargo holder; preserve native child/stock/occupancy refusals |
| `interior pack ITEM --at X Z [--floor FLOOR]` | Native designer pack action, box identity/location, contents and staged status |
| `interior transfer SOURCE ENTRY TARGET [--amount N]` | Native existing-holder transfer, requested/moved/remainder quantities and refusal; no synthetic storage |
| `interior sell ITEM` / `interior for-sale ITEM on\|off` | Distinct sale flows, affected group, native displayed quote/confirmation and staged or finalized result |
| `interior discard ITEM` | Native discard confirmation, affected group/contents and final removal result; keep separate from selling |
| `interior design open\|status\|apply\|discard\|undo\|redo` | Native designer session state, pending changes and displayed settlement; apply/discard retain the game's confirmation semantics |
| `interior catalogue [--search TEXT] [--category CATEGORY]` | Native eligible catalogue entries for this context, preview/description, target type and ordinary displayed purchase information |
| `interior add CATALOGUE_ENTRY [placement target/options]` | Native furniture-tool selection and reversible placement, with staged purchase status |
| `interior duplicate ITEM [--single\|--group] [placement target/options]` | Native duplication with newly created IDs, relationships and staged purchase status |
| `interior materials wall\|floor` | Purchasable native presets, colour variants and previews; no asset dump or hidden scoring data |
| `interior finish SURFACE PRESET [--variant VARIANT]` | Reversible finish change on the exact native element/face and serialized result; report the full affected set if the native paint action affects a group |
| `interior colour ITEM --channel CHANNEL --colour COLOUR` / `interior colour ITEM --reset` | Supported native channel change/reset, before/after value and undo state |
| `interior colour-copy SOURCE TARGET` | Native eyedropper behavior restricted to compatible channels |
| `interior configure ITEM --product PRODUCT\|--text TEXT\|--recipe RECIPE` | Valid native choice, resolved setting and requirement/producer warnings |
| `interior queue ITEM show\|reset` / `interior queue ITEM set X Z [X Z ...]` | Ordered native queue anchors, actual positions and validation/refusal with undo |
| `interior view floor FLOOR\|walls MODE\|light TIME` | Native view state and selected floor; lighting preview distinguished from elapsed game time |
| `ui text FIELD TEXT` | Native text input change/end-edit result for a freshly listed eligible field, enabling searches, quantities, names and signs |

A lasting preview session is not required for the first version. Keep individual placement/move calls atomic and freeze mouse-follow handling for their internal work. If interactive staging is added later, it needs an explicit owner/token and cancellation on disconnect, load or unload. Never leave an unattended ghost following the real mouse.

Designer operations must execute native reversible actions and settle through the native close path. Calling private colour, pack, duplicate or spawn helpers by themselves is not a complete player action. Provide the same native confirmations and eligibility checks a player receives. Do not make every command a raw transform write or maintain a second money/undo implementation.

### Orders and blueprints

| Proposed command | Output |
|---|---|
| `furniture order options CONTACT` | Discovered contact's actual offered goods, eligible destinations and currently offered delivery slots |
| `furniture order CONTACT --to ADDRESS --slot SLOT --item ENTRY --quantity N` | Native order confirmation/result and contract handle, with displayed purchase details and notices |
| `furniture deliveries` / `furniture cancel ORDER` | Pending/delivered/refused order state and native cancellation result; delivered cargo source is selectable through `interior cargo` |
| `blueprint list [--compatible]` / `blueprint inspect ID` | Local/native library entries, building type/size/version compatibility, missing-mod warnings and the game's preview |
| `blueprint save NAME` | Native local-save completion, saved identity and thumbnail; distinguish saving from applying pending room edits |
| `blueprint install ID --to ADDRESS --slot SLOT` | Native installation order/confirmation, replacement warning, current quote and eventual contract outcome |
| `blueprint status [ORDER]` | Scheduled, complete or refused installation with native reason and refreshed interior revision after completion |
| `blueprint editor open ID` / `blueprint editor new TYPE SIZE` | Later separate editor context, followed by the same interior controls where native tools permit them; save and exit through `BlueprintCreatorSystem` |

Catalogue prices, order quotes, quantities in the player's own containers and costs actually shown by the game are ordinary player observations. These commands must not expose price formulas, resale multipliers, profit/capacity optimization tables, hidden interior-score weights, asset-wide requirement thresholds or mod balance tables. Geometry and the native checklist for the room being furnished are control information. Keep any future referee diagnostics outside `tools/play`.

## 5. Recommended build order

1. **Establish identity and observation.** Build `interior look`, items, surfaces, a floor map and requirements. Include every placed item, fixed structure, floor identity and uncertainty. Improve `tools/play use` to print the already available coordinates where helpful. This makes the current bed placement reviewable before adding more mutations.
2. **Repair the shared floor placement path.** Resolve the target floor/surface explicitly, isolate live input, set snap/rotation modes, preserve callbacks and return the actual pose/ID. Verify cancellation through item/cargo conservation and placement flags. Add move-by-ID through the same adapter. Do not widen collider ancestry again as a substitute for floor identity.
3. **Add attachments, walls and ceilings.** Ship attachment-slot observation with placement, covering a counter/register or tabletop setup, a desk/computer/seat group, wall decoration and ceiling lights. Validate group moves and parent/child persistence. Support free yaw and free position where native rules allow them.
4. **Complete acquisition and removal.** Integrate the separate held-box and hand truck work. Add chosen cargo sources, normal furniture pickup/packing, cargo transfer and sale. Complete furniture order fields and delivery status. Test the whole purchase-to-building-to-placement loop, including mixed contents and refusals.
5. **Expose designer transactions and decoration.** Add open/status/apply/discard/undo/redo, then catalogue purchase, duplicate, colour and wall/floor finishes. First close the missing-tool-implementation evidence gap through locally available assemblies/source or later instrumentation. Preserve native action costs, eligibility and serialization.
6. **Make furnished businesses operational.** Add product/text/recipe configuration, queue editing, security and access reports. Expose the actual per-type native checklist across all player-available types, rather than testing only a shop. Add sampled `spots` only once temporary placement rollback is reliable.
7. **Add blueprint workflows.** Local blueprint save, compatible preview and paid installation come before standalone creator support. The separate creator scene is useful for reusable layouts but is not a dependency for furnishing a home or business in play.

For a later authorized game check, require evidence across: a home with model-floor tiles; a business floor; upper/lower floors and a void/stair boundary; snapped/free rotation; wall backing; ceiling restrictions; compatible/incompatible attachments; group moves; occupied and stocked furniture; preview refusal/cancel without loss; colour and finish undo/redo; native apply/discard settlement; delivery and paid installation. Re-enter and reload to check identities, parent links, pose, stock choices and finishes persist. Verify that a usable bed and staffed or player-operated business station can actually be reached after navigation updates. Mock responses alone cannot establish these results. No such checks were run for this evaluation.

## 6. Open questions for Peter

1. Should the series use every normal native purchasing route, including direct designer catalogue purchases, or require physical shop/delivery acquisition for some episodes? Both are game paths, but the choice affects the first complete workflow.
2. Is a metric room map with item-specific valid candidates acceptable as the agent's equivalent of the placement grid and visual inspection? Should the agent choose every pose itself, or may `spots` suggest candidates without ranking their business value?
3. Does the first series need the standalone blueprint creator, or are local blueprint saves and paid installation during city play sufficient initially?
4. Should the agent use built-in/library blueprints, only blueprints it creates, or both? Workshop upload and publication are separate actions and are not needed for interior control completeness.
5. Which first demonstrations matter most: a fully decorated home, a retail checkout/display layout, a food-service counter, an office workstation group, or a factory? The adapters should remain general, but this selects the first visible acceptance scenes.
6. Should the agent be allowed to redesign occupied/open businesses using the game's existing restrictions and notices, or should its episode procedure close the business first? No extra restriction is required by this evaluation.
7. What visual review should end a decorating episode: the ordinary game camera, a designer view, or a small set of room shots? Structured geometry can establish control coverage; Peter's visual review is still needed to judge the decoration.

These choices do not block the observation and placement-correctness work. The unresolved technical questions are the missing designer tool bodies, asset-specific requirements and finish catalogues, runtime floor/model geometry, attachment occupancy behavior and native rollback under failure. They need source or later game evidence, not guesses from Peter.

## Suggestion under discussion (17 October 2026)

Peter's suggestion: give the agent the room's floor plan from the game's own placement grid squares, and let it place items one after another by naming squares on that plan instead of raw world points. Not decided yet.

Refined in discussion: the game has one clear base grid, so the plan uses that grid's squares. A building has at most two floors (rare); the plan always shows the floor the player stands on, so the same code covers both. Additions talked through: per item, the squares it covers and the square someone uses it from, with reachability; a check that tries a spot without placing; wall segments and stacking slots as small lists beside the plan; a text map for overview plus a list for exact work; geometry only, no layout advice.
Grids come in standard layouts (the game's building sizes), so a plan is known per layout and the same squares mean the same places in every building of that layout.
