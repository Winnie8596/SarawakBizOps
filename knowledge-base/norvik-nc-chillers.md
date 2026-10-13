---
slug: norvik-nc-chillers
title: Norvik NC-series Industrial Chillers — Service Manual
brand: Norvik
models: [NC-450, NC-300]
version: 3.1
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

Work through a fault in the order given. The causes under each alarm are listed most likely first, based on field
returns from tropical sites, and the checks follow the same order. Read the alarm history before changing anything:
the time of day and the alarms that came just before often point straight at the cause. Record what you measured and
what you changed in the work order, so the next technician can see whether a fault is coming back.

### E14 High head pressure

**Meaning on NC-series chillers:** the high-side pressure exceeded the cut-out (19.5 bar) and the compressor stopped.

**What the operator sees.** The compressor stops with E14 on the display while the condenser fans keep running for a
few minutes. On most sites the alarm appears in the early or mid afternoon, clears itself once the unit has cooled,
and comes back the next afternoon. After the third trip in one hour the alarm latches and the cold room starts to
warm, which is usually when the call comes in.

Typical causes, most likely first:

1. Condenser coil fouled with dust or debris, common in afternoons when ambient temperature is highest.
2. A condenser fan not running, or running slowly because its motor bearings are worn (listen for grinding noise).
3. Refrigerant overcharge after a recent service.
4. Non-condensable gas in the circuit.

**Reading the alarm history.** E14 that only appears between noon and 4 pm, with no other alarms, points to the coil
or a fan: the circuit is healthy but cannot reject heat on the hottest part of the day. E14 at any time of day, or
E14 shortly after a refrigerant service, points to an overcharge or non-condensable gas. E14 following an E30 means
the unit restarted against a high pressure after a flow interruption and is usually not a condenser fault at all.

Checks:

- With the unit isolated, inspect the condenser coil face. Clean it from the inside out with low-pressure water or a
  coil cleaner; never use a pressure washer, which flattens the fins.

- Look at the whole coil, not only the face you can reach. On units near a road or a sawmill, dust packs into the
  bottom third of the coil and is hidden behind the base rail. Leaves and plastic sheeting drawn onto the coil by the
  fans block whole sections at once and are easy to miss from the front.

- Restore power and confirm every condenser fan starts on demand. A fan that hums but does not turn points to a failed
  run capacitor; a noisy fan points to worn motor bearings (deep-groove ball bearing 6205-2RS, part BRG-6205-2RS).

- Count the fans. The NC-450 has eight condenser fans in two rows (the NC-300 has six). On demand they stage on in
  pairs, so at full load in the afternoon every fan should be running. One stopped fan on the NC-300 removes about a
  sixth of the condenser capacity, which is enough to cause E14 on a hot day.

- If the coil is clean and every fan runs, compare the liquid line temperature with the condensing temperature read
  from the high-side pressure. Subcooling above 10 K at full load suggests an overcharge. Do not remove refrigerant
  yourself unless you are certified; record the readings and pass the job to a refrigerant handler.

- Non-condensable gas shows as a high-side pressure higher than the condensing temperature explains, even with the
  unit stopped and the coil at ambient temperature. It is almost always introduced during a repair and needs recovery,
  evacuation and recharge by a certified handler.

**After the repair.** Record head pressure through a full afternoon cycle after the repair. It should stay below
17 bar at 35 °C ambient. Reset the latched alarm only after the cause is fixed, and note in the work order which
fans were running and the head pressure at the hottest reading, so the next visit has a baseline to compare against.

### E21 Low suction pressure

**Meaning:** suction pressure stayed below 2.0 bar for 60 seconds.

**What the operator sees.** The compressor stops with E21, usually soon after start-up or when the load drops, and
the chilled-water outlet may read colder than the set point just before the trip. Repeated E21 can be followed by an
evaporator freeze warning on the controller.

Check the chilled-water flow first: a closed valve, a blocked strainer or a stopped chilled-water pump starves the
evaporator. Then check for a refrigerant leak with an electronic detector around the expansion valve and the sight
glass. Bubbles in the sight glass at full load indicate low charge.

Typical causes, most likely first:

1. Low chilled-water flow: a partly closed isolating valve, a blocked strainer, or air in the chilled-water circuit.
2. Low refrigerant charge from a leak, most often at flare joints, the sight glass or a service valve cap.
3. Electronic expansion valve stuck partly closed, or its sensor reading wrongly.
4. Chilled-water set point lowered below the design value by an operator.

Checks:

- Walk the chilled-water circuit from the evaporator outlet back to the pump. Every isolating valve should be fully
  open, the strainer pressure drop should be within the value on the commissioning sheet, and the pump should be
  running at its normal current. A strainer that has not been cleaned since commissioning is a common finding.

- Bleed air from the high points of the chilled-water circuit. Air pockets reduce flow without any visible fault and
  are common after pipework changes in the cold room.

- Check the set point on the controller against the commissioning sheet. A set point below 5 °C leaving water on a
  unit designed for 7 °C can drive suction pressure down and cause E21 at light load.

- Leak test with an electronic detector, unit running, moving slowly around every joint on the low side and the
  expansion valve body. Oil staining at a joint is a strong hint. Record any leak and pass it to a certified handler;
  do not top up a leaking circuit.

- If flow and charge are correct, read the expansion valve opening on the controller service screen. A valve that
  stays below 20% open while suction pressure is low points to the valve or its suction sensor, not the charge.

**After the repair.** Run the unit at full load for 30 minutes and confirm suction pressure stays above 2.5 bar and
the sight glass is clear. Reset the alarm only after the cause is fixed.

### E30 Chilled-water flow switch open

The paddle flow switch opened while the unit was running. Confirm the chilled-water pump is running and the strainer
is clean before resetting. Do not bridge the flow switch: running without flow can freeze and split the evaporator.

**What the operator sees.** The compressor stops at once with E30, often with no warning. On sites where the
chilled-water pump is started by another controller, E30 frequently appears when that controller switches pumps or
loses power for a moment.

Typical causes, most likely first:

1. The chilled-water pump stopped or tripped, or a duty/standby changeover left a gap in flow.
2. A blocked strainer reducing flow below the switch setting.
3. Air in the circuit making the paddle flutter.
4. The paddle bent, worn or set for the wrong pipe size after a repair.

Checks:

- Confirm the pump is running and check its overload for a recent trip. If the site changes over pumps on a timer,
  check whether the alarm history time matches the changeover time.

- Clean the strainer and bleed air from the high points.

- With the unit isolated and the circuit drained to below the switch, remove the switch and check that the paddle is
  straight and that its length matches the pipe size marked on the switch body. A paddle trimmed for a smaller pipe
  will not close reliably on the NC-450 (NC-300) header.

- Refit, refill and bleed, and confirm the switch closes within ten seconds of the pump starting.

### Compressor short-cycling and not holding temperature

**What the operator sees.** The cold room or process slowly warms although the chiller is running, and the compressor
starts and stops every few minutes instead of running steadily. There may be no alarm on the display at all, which
is why this fault is often reported late.

The controller allows at most six compressor starts per hour (four on the NC-300); short-cycling that hits this limit
shows as a "start inhibit" message on the status line, not as an alarm code. Each start puts heavy current through the
motor windings and contactor, so a unit left short-cycling for weeks wears its contactor and can damage the motor.

Typical causes, most likely first:

1. Chilled-water flow too low, so the leaving water temperature falls quickly and the compressor stops on the set
   point, then restarts as soon as the water warms.
2. Set point differential narrowed below the design value (the factory value is 2.0 K).
3. Low refrigerant charge, so the compressor stops on low suction pressure before E21 latches.
4. A faulty leaving water temperature sensor (PT100, part SNS-PT100) reading high or jumping.
5. Worn compressor contactor contacts dropping out under load.

Checks:

- Watch at least three full cycles and write down, for each, the leaving water temperature at start and stop, the
  suction pressure and the run time. A run time under three minutes with the leaving water falling fast points to low
  flow or a narrow differential; a stop with suction pressure near 2.0 bar points to low charge.

- Check the set point and differential on the controller against the commissioning sheet and correct them if someone
  has changed them. Record who reported the change if the site keeps a log.

- Check chilled-water flow as for E21: valves open, strainer clean, air bled, pump at its normal current.

- Compare the controller's leaving water reading with a calibrated thermometer in the test pocket next to the sensor.
  A difference of more than 0.5 K, or a reading that jumps while the thermometer is steady, means the sensor or its
  lead is faulty. Check the three-wire connection at the terminal block first; a loose lead is common. Replace the
  sensor with SNS-PT100 if the connection is good.

- With the unit isolated, inspect the compressor contactor contacts. Pitted or burnt contacts should be replaced as a
  set, never dressed.

**After the repair.** The compressor should run for at least ten minutes per cycle at normal load, and the start
counter should stay below the hourly limit through a full shift.

### Condenser fan noise

**What the operator sees.** One fan is louder than the others. The noise is loudest at start-up and in the afternoon
when the fan runs longest. Left alone, the bearing seizes and the fan stops, which then
shows up as E14.

A rising whine or grinding from one fan, getting worse over weeks, is almost always the fan motor bearings. Replace
both bearings of that motor (2 × BRG-6205-2RS) and check the fan blade for cracks and balance before refitting.

Checks and repair:

- Identify the fan by listening at each guard with the unit running, then isolate the unit and lock it off before
  opening any guard. Remember that fans start on pressure demand even when the compressor is stopped.

- Spin the fan by hand. Roughness, a gritty feel or side play at the hub confirms bearing wear. A fan that turns
  freely but hums without starting under power points to the run capacitor instead.

- Remove the motor, press out both bearings and fit two new BRG-6205-2RS bearings. Replace both even if only one feels
  rough: they have run the same hours.

- Inspect the blade for cracks at the root and for dust build-up on one side, which throws it out of balance and
  shortens bearing life. Clean the blade and replace it if it is cracked.

- Refit the motor and guard, restore power and check the fan runs quietly through a full afternoon cycle.

## Maintenance schedule

| Interval | Task |
|---|---|
| Monthly | Inspect and clean condenser coils; check fan operation; read alarm history |
| Quarterly | Check compressor oil level and colour; tighten electrical terminals (isolated) |
| Yearly | Oil analysis; leak test; calibrate pressure transducers |
