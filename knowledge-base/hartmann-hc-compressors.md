---
slug: hartmann-hc-compressors
title: Hartmann HC-series Rotary Screw Air Compressors — Service Manual
brand: Hartmann
models: [HC-75, HC-55]
version: 2.4
---
# Hartmann HC-series Rotary Screw Air Compressors — Service Manual

Covers the Hartmann HC-75 (75 kW) and HC-55 (55 kW) oil-injected rotary screw compressors. All content is synthetic
and written for the SarawakBizOps demonstration.

## Safety

Stop the compressor, isolate it at the main isolator and lock it off before removing any panel. The air receiver and
oil separator stay pressurised after stopping: open the manual blow-down valve and wait until the gauge reads 0 bar
before loosening any fitting on the air or oil circuit. Oil in the separator can be above 80 °C.

## Controller alarms

### E14 Motor overload trip

**Meaning on HC-series compressors:** the thermal overload relay on the main motor tripped. (This is not the same
fault as E14 on other Hartmann or Norvik equipment.)

Typical causes, most likely first:

1. Clogged intake air filter, making the motor work harder (worst during hot afternoon shifts).
2. Overload relay set below the motor's nameplate current, or relay contacts pitted.
3. Low supply voltage on one phase.
4. Separator element blocked, raising discharge pressure.

Checks:

- Inspect the intake filter element (part FLT-AIR-HC75 for HC-75 and HC-55). Replace it if the restriction indicator
  shows red or the element is visibly dirty; never run without it.
- With the compressor isolated, check the overload relay setting against the motor nameplate (HC-75: 128 A;
  HC-55: 96 A) and inspect the contacts. Replace a relay with pitted contacts (part REL-OVL-25A is the control-circuit
  relay; the main motor relay is a site-specific item).
- Measure running current on all three phases after the repair; they should be within 5% of each other.

### E22 High discharge temperature

Discharge temperature exceeded 110 °C. Check the oil level in the sight glass with the unit stopped, then check the
oil cooler for blocked fins and confirm the cooling fan runs. Low oil level is the most common cause.

### E35 Separator differential pressure high

The pressure drop across the oil separator element exceeded 1.0 bar. The element is saturated and must be replaced.

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
