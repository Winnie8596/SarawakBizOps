---
slug: kestrel-kb-10t-boiler-v2
title: Kestrel KB-10T Steam Boiler — Operation and Maintenance Manual
brand: Kestrel
models: [KB-10T]
version: 2.0
supersedes: kestrel-kb-10t-boiler-v1
---
# Kestrel KB-10T Steam Boiler — Operation and Maintenance Manual

Kestrel KB-10T fire-tube steam boiler, 10 tonnes of steam per hour, maximum working pressure 10 bar. All content is
synthetic and written for the SarawakBizOps demonstration. Edition 2 replaces edition 1; the manhole gasket procedure
has changed.

## Safety

The boiler must be operated by a certified steam engineer. Before opening the manhole or any pressure part, shut the
main steam stop valve, isolate the burner and feed pump electrically and lock them off, and allow the boiler to cool
until the pressure gauge reads 0 bar and the shell is below 50 °C. Steam and hot water can cause severe burns: never
open a drain or vent towards a person.

## Manhole gasket replacement

**Changed in edition 2:** hot re-tightening at working pressure is no longer permitted.

1. Confirm the boiler is cold and at 0 bar, and the vent valve is open.
2. Remove the manhole yoke nuts and lower the cover.
3. Remove the old gasket and clean both faces; check the faces for pitting.
4. Fit a new graphite manhole gasket (part GSK-BLR-10T). Never reuse a gasket.
5. Tighten the yoke nuts to 120 N·m with a torque wrench, in two passes, with the boiler cold.
6. Fill and fire the boiler, and check the manhole for leaks at 2 bar before raising to working pressure. If it leaks,
   shut down, cool to 0 bar and replace the gasket; do not tighten under pressure.

## Feed-water pump loses prime

If the feed-water pump loses prime after an overnight shutdown, the foot valve on the suction line is usually not
seating and the line drains back to the tank. Clean the foot valve seat and replace its spring, then prime the pump
and confirm it holds prime over two shutdowns.

## Alarms

### E14 Low water level

**Meaning on KB-series boilers:** the second low-water probe detected water below the safe level and the burner was
locked out. Do not add feed water to a hot boiler that has run dry: allow it to cool and call the manufacturer.

### E31 Flame failure

The burner did not detect a flame within the safety time. Check the fuel supply valve is open, the oil filter is clean
and the ignition electrode gap is 3 mm. Reset once only; a second failure needs a qualified burner technician.
