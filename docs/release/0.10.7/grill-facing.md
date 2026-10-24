# Cart equipment facing and counter-side service

All imported equipment fronts point Unity +Z (Blender -Y). Controls, doors and working openings use +Z except PopcornMachine and IceCreamCabinet, whose operator doors use -Z. The per-prefab axes are recorded in art/equipment/*/spec.json and applied by CartStance.EquipmentYaw in VehicleDisplays. The dedicated cart fridge is also mounted on the worker side. Register yaw and food-truck anchors/child rotations remain unchanged.

The old adapter rotated every equipment child by queueSide * 90 degrees, like the customer-facing register. CartAssets equipment anchors have identity rotation, so no compensating yaw existed. The old ground search also allowed end-of-cart service and up to 3 m along-counter drift.

A cart now requires safe pedestrian ground behind the counter, within 0.35 m along the preferred place and at most 1.5 m across the cart. Serving places are level with the register; waiting crew use the same bounds about their own waiting place. No end-of-cart fallback. Queue layout considers both sides and rejects a side without a safe server place before counting customer places. Existing customer road exclusions remain: customers never stand in the road to rescue a pavement-only server. If neither side works, no customer line is shown and unsafe crew remain hidden. Player service retains its complete reachable-path requirement. Trucks do not enter these cart branches.

Native verification and final package receipt: pending. Historical kerb-safety.md records the superseded end/drift-permitting behavior.
