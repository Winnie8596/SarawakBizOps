---
slug: norvik-nc-chillers
title: Norvik NC-series Industrial Chillers — Service Manual
brand: Norvik
models: [NC-450, NC-300]
version: 3.0
---
# Norvik NC-series Industrial Chillers — Service Manual

This manual covers the Norvik NC-450 (450 kW) and NC-300 (300 kW) air-cooled screw chillers. Where a value
differs between models, the NC-450 value is given first and the NC-300 value in brackets. All content is synthetic
and written for the SarawakBizOps demonstration.

## Safety

### Before any work inside the unit

Isolate the chiller at the main isolator on the electrical panel and apply your personal lock and tag (lockout/tagout).
Wait five minutes for the compressor drive capacitors to discharge, then confirm zero voltage between L1, L2, L3 and
earth with a tested voltage detector. The unit is supplied at 415V three phase.

The refrigerant circuit holds R134a at up to 16 bar on the high side. Never open a refrigerant connection unless you
are a certified refrigerant handler with recovery equipment on site.

### Hot and moving parts

The discharge line and oil separator can exceed 90 °C during operation. Condenser fans can start automatically on
pressure demand even when the compressor is stopped: isolate the unit before opening the fan guards.

## Controller and alarms

The NC controller shows alarm codes on the front display. An alarm that clears itself three times in one hour latches
and needs a manual reset after the cause is fixed. The last 50 alarms are kept in the alarm history menu.

## Troubleshooting

### E14 High head pressure

**Meaning on NC-series chillers:** the high-side pressure exceeded the cut-out (19.5 bar) and the compressor stopped.

Typical causes, most likely first:

1. Condenser coil fouled with dust or debris, common in afternoons when ambient temperature is highest.
2. A condenser fan not running, or running slowly because its motor bearings are worn (listen for grinding noise).
3. Refrigerant overcharge after a recent service.
4. Non-condensable gas in the circuit.

Checks:

- With the unit isolated, inspect the condenser coil face. Clean it from the inside out with low-pressure water or a
  coil cleaner; never use a pressure washer, which flattens the fins.
- Restore power and confirm every condenser fan starts on demand. A fan that hums but does not turn points to a failed
  run capacitor; a noisy fan points to worn motor bearings (deep-groove ball bearing 6205-2RS, part BRG-6205-2RS).
- Record head pressure through a full afternoon cycle after the repair. It should stay below 17 bar at 35 °C ambient.

### E21 Low suction pressure

**Meaning:** suction pressure stayed below 2.0 bar for 60 seconds.

Check the chilled-water flow first: a closed valve, a blocked strainer or a stopped chilled-water pump starves the
evaporator. Then check for a refrigerant leak with an electronic detector around the expansion valve and the sight
glass. Bubbles in the sight glass at full load indicate low charge.

### E30 Chilled-water flow switch open

The paddle flow switch opened while the unit was running. Confirm the chilled-water pump is running and the strainer
is clean before resetting. Do not bridge the flow switch: running without flow can freeze and split the evaporator.

### Condenser fan noise

A rising whine or grinding from one fan, getting worse over weeks, is almost always the fan motor bearings. Replace
both bearings of that motor (2 × BRG-6205-2RS) and check the fan blade for cracks and balance before refitting.

## Maintenance schedule

| Interval | Task |
|---|---|
| Monthly | Inspect and clean condenser coils; check fan operation; read alarm history |
| Quarterly | Check compressor oil level and colour; tighten electrical terminals (isolated) |
| Yearly | Oil analysis; leak test; calibrate pressure transducers |
