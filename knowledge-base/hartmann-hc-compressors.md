---
slug: hartmann-hc-compressors
title: Hartmann HC-series Rotary Screw Air Compressors — Service Manual
brand: Hartmann
models: [HC-75, HC-55]
version: 2.5
---
# Hartmann HC-series Rotary Screw Air Compressors — Service Manual

Covers the Hartmann HC-75 (75 kW) and HC-55 (55 kW) oil-injected rotary screw compressors. All content is synthetic
and written for the SarawakBizOps demonstration.

## Safety

Stop the compressor, isolate it at the main isolator and lock it off before removing any panel. The air receiver and
oil separator stay pressurised after stopping: open the manual blow-down valve and wait until the gauge reads 0 bar
before loosening any fitting on the air or oil circuit. Oil in the separator can be above 80 °C.

## Controller alarms

Each section below covers one alarm: what it means, what the operator usually reports, the likely causes in order,
and the checks. The controller keeps the last 30 alarms with the time and the discharge pressure and temperature at
the moment of the trip; read them before you start. A compressor that trips at the same time every day almost always
has a heat or load problem, not a failed component.

### E14 Motor overload trip

**Meaning on HC-series compressors:** the thermal overload relay on the main motor tripped. (This is not the same
fault as E14 on other Hartmann or Norvik equipment.)

**What the operator sees.** The compressor stops with E14 and the air pressure on the site falls. The relay can be
reset after a few minutes, and the compressor then runs normally for a while. On most sites the trips happen in the
hot afternoon shift, when air demand and plant-room temperature are both at their highest, and do not happen at
night.

Typical causes, most likely first:

1. Clogged intake air filter, making the motor work harder (worst during hot afternoon shifts).
2. Overload relay set below the motor's nameplate current, or relay contacts pitted.
3. Low supply voltage on one phase.
4. Separator element blocked, raising discharge pressure.

**Reading the alarm history.** E14 with a discharge pressure at or near the normal set point points to the motor side:
filter, relay or supply. E14 with a discharge pressure well above the set point, or an E35 shortly before, points to
the separator. E14 that follows an E22 means the unit has been running hot for some time and the cooling side should
be checked as well.

Checks:

- Inspect the intake filter element (part FLT-AIR-HC75 for HC-75 and HC-55). Replace it if the restriction indicator
  shows red or the element is visibly dirty; never run without it.

- Check where the compressor draws its air from. A plant room with poor ventilation can reach 45 °C in the afternoon,
  and hot intake air both reduces output and raises motor load. If the room is hot, check the ventilation fans and
  louvres as well as the filter.

- With the compressor isolated, check the overload relay setting against the motor nameplate (HC-75: 128 A;
  HC-55: 96 A) and inspect the contacts. Replace a relay with pitted contacts (part REL-OVL-25A is the control-circuit
  relay; the main motor relay is a site-specific item).

- Do not raise the relay setting above the nameplate current to stop the trips. The relay is protecting the motor;
  a higher setting only moves the failure from the relay to the windings.

- Measure the supply voltage on all three phases at the isolator with the compressor running at full load. A phase
  more than 2% below the others makes the motor draw extra current on the other two and can trip the relay without any
  fault in the compressor. Report an unbalanced supply to the site electrician.

- Measure running current on all three phases after the repair; they should be within 5% of each other.

**After the repair.** Run the compressor at full load through the hottest part of the next shift and record the
running current every hour. It should stay below the nameplate value throughout. Note the restriction indicator
reading on the new filter so the next visit can see how fast it is loading.

### E17 Short-cycling under load

**Meaning on HC-series compressors:** the motor started more than ten times in one hour and further starts are
blocked for 15 minutes to protect the motor.

**What the operator sees.** The compressor loads and unloads, or starts and stops, every minute or two. The site air
pressure swings between the cut-in and cut-out pressures. Often the operator reports this as "it keeps tripping",
and the alarm history shows E17 mixed with E14, because frequent starts heat the motor and the overload relay.

Typical causes, most likely first:

1. Air receiver too small for the demand, or its drain valve stuck open so the receiver never fills properly.
2. Cut-in and cut-out pressures set too close together (the factory gap is 1.0 bar).
3. A leaking or sticking minimum pressure valve.
4. The pressure transducer giving a noisy reading.
5. A large air leak downstream that empties the system quickly.

Checks:

- Watch three cycles and write down the cut-in pressure, the cut-out pressure and the time between them. If the
  pressure rises to cut-out in under 30 seconds, the receiver is too small or partly full of water.

- Drain the receiver fully and check the automatic drain. A drain stuck open hisses continuously; a drain stuck closed
  lets the receiver fill with condensate, which takes up air volume. Both are common in humid sites.

- Check the pressure settings on the controller against the commissioning sheet. Restore the 1.0 bar gap if someone
  has narrowed it.

- With the compressor stopped and the receiver blown down to 0 bar, inspect the minimum pressure valve. It must
  hold at least 4 bar on a restart; a valve that leaks back lets the separator empty and the unit restart unloaded.

- Walk the air lines with the plant stopped and listen for leaks. A site that loses more than 1 bar in ten minutes
  with no demand has leaks large enough to cause short-cycling on their own.

- Compare the controller's pressure reading with a test gauge on the receiver. If the controller reading jumps while
  the gauge is steady, check the transducer cable and connector before replacing the transducer.

**After the repair.** The compressor should make no more than six starts per hour at normal site demand. Clear E17
and check the alarm history for E14 at the end of the next shift.

### E22 High discharge temperature

Discharge temperature exceeded 110 °C. Check the oil level in the sight glass with the unit stopped, then check the
oil cooler for blocked fins and confirm the cooling fan runs. Low oil level is the most common cause.

**What the operator sees.** The compressor stops with E22 after a long run, usually in the afternoon, and restarts
normally once it has cooled. The discharge temperature on the display climbs steadily over the shift instead of
settling at its usual 80–90 °C.

Typical causes, most likely first:

1. Low oil level, from carry-over through a saturated separator or from a leak.
2. Oil cooler fins blocked with dust and oil mist.
3. Cooling fan not running, or running slowly.
4. Thermostatic valve stuck, sending oil around the cooler instead of through it.
5. Oil past its service life, which loses its ability to carry heat away.

Checks:

- With the unit stopped and blown down to 0 bar, read the oil level in the sight glass. Top up with the specified
  compressor oil only; never mix oil brands. If the level was low, look for the cause: oil in the air lines (see
  Oil carry-over) or a wet patch under the unit.

- Inspect the oil cooler from both sides with a torch. Oil mist and dust form a felt on the fins that looks clean from
  a distance. Clean with a degreaser and low-pressure air from the fan side out.

- Restore power and confirm the cooling fan runs whenever the motor runs.

- After 20 minutes at load, feel the oil pipes either side of the cooler with the back of your hand, carefully. Both
  should be hot and the outlet should be clearly cooler than the inlet. If the cooler stays cool, the thermostatic
  valve is not opening; replace its element.

- Check the hours since the last oil change against the service intervals. Overdue oil should be changed together with
  the oil filter.

### E35 Separator differential pressure high

The pressure drop across the oil separator element exceeded 1.0 bar. The element is saturated and must be replaced.

**What the operator sees.** Usually nothing at first: E35 is a warning for some time before it causes other faults.
If it is ignored, the motor current rises because the compressor is pushing against the blocked element, and E14
trips follow. Oil in the downstream air lines is often reported at the same time.

Checks:

- Note the differential pressure on the controller at full load. Between 0.6 and 1.0 bar the element is near the
  end of its life and should be replaced at the next service; above 1.0 bar it must be replaced now.

- With the unit stopped, isolated and blown down to 0 bar, replace the separator element and its gaskets. Clean the
  scavenge line orifice at the same time; a blocked orifice saturates a new element within weeks.

- After restart, the differential pressure should read below 0.3 bar at full load.

## Oil carry-over

Oil in the downstream air lines usually means a saturated separator element or a blocked scavenge line orifice. Replace
the separator element, clean the scavenge orifice with a soft wire, and drain oil from the air lines and receiver.
Check that the minimum pressure valve holds at least 4 bar: below that the separator cannot work properly.

## Service intervals

| Hours | Task |
|---|---|
| 2,000 | Replace intake filter element; check belt tension; drain condensate traps |
| 4,000 | Replace oil filter and oil; replace separator element |
| 8,000 | Inspect motor bearings; check safety valve |
