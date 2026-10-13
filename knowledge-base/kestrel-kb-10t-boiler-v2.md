---
slug: kestrel-kb-10t-boiler-v2
title: Kestrel KB-10T Steam Boiler — Operation and Maintenance Manual
brand: Kestrel
models: [KB-10T]
version: 2.1
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

## Troubleshooting

Faults on the KB-10T fall into three groups: water side (feed pump, level control), steam side (pressure control and
safety valves) and firing side (burner and flame). Start with the boiler log: the steam engineer records pressure,
water level and blowdowns every shift, and most faults show up there before they cause an alarm. Any fault that
involves the safety valve, the low-water probes or the flame safeguard must be reported to the site's certified steam
engineer before the boiler is returned to service.

### Feed-water pump loses prime

If the feed-water pump loses prime after an overnight shutdown, the foot valve on the suction line is usually not
seating and the line drains back to the tank. Clean the foot valve seat and replace its spring, then prime the pump
and confirm it holds prime over two shutdowns.

**What the operator sees.** On the morning start-up the feed pump runs but the water level in the gauge glass does not
rise, and the pump is noisier than usual. If nobody notices, the boiler reaches the first low-water level and the
burner goes to low fire. Priming the pump by hand fixes it for the day, and the fault returns after the next
overnight stop.

Typical causes, most likely first:

1. Foot valve on the suction line not seating, letting the line drain back to the feed tank.
2. Feed tank temperature too high, so the water flashes to steam at the pump suction (cavitation).
3. Air drawn in at a suction line joint or the pump gland.
4. Suction strainer blocked with scale.

Checks:

- With the pump isolated and locked off, close the suction valve and remove the foot valve. Look for scale or debris
  on the seat and check that the spring closes the valve firmly. Clean the seat and replace the spring as a matter of
  course.

- Read the feed tank thermometer. Above 90 °C the water flashes at the pump suction and the pump loses prime even with
  a good foot valve. Check the condensate return and the tank vent; a blocked vent lets the tank temperature rise.

- With the pump running, brush soapy water over the suction joints and the gland. Bubbles being drawn in show an air
  leak; tighten or remake the joint.

- Clean the suction strainer.

**After the repair.** Prime the pump, run the boiler normally and confirm the pump holds prime over two overnight
shutdowns before closing the job.

### Pressure gauge erratic or safety valve lifting

**What the operator sees.** The steam pressure gauge needle jumps or swings instead of moving smoothly, and the safety
valve lifts (vents steam with a loud roar) at times when the boiler should be at or below its normal working
pressure. Often this happens overnight or at the start of a shift, when the steam load changes quickly.

A safety valve that lifts is doing its job. Never gag, wedge or adjust a safety valve to stop it lifting, and never
fire the boiler with a safety valve isolated. Only a certified person may adjust or reseal a safety valve, and a
valve that has been adjusted must be tested and resealed before the boiler returns to service.

Typical causes, most likely first:

1. The pressure gauge siphon (the coiled pipe under the gauge) blocked or empty of water, so the gauge reads
   wrongly and the operator, or the controller, reacts to a false pressure.
2. The burner pressure control switch set too close to the safety valve set pressure, or drifting, so the burner does
   not go to low fire in time when the steam load drops.
3. Steam load dropping suddenly (a large user shutting its valve) while the burner is at high fire.
4. The safety valve seat leaking or the valve set below its stamped pressure.

Checks:

- Compare the boiler gauge with a calibrated test gauge on the test connection. With the boiler at steady pressure,
  the two should agree within 0.2 bar. A gauge that disagrees or swings while the test gauge is steady must be
  replaced, together with its siphon if the siphon is blocked.

- With the boiler cold and at 0 bar, isolate the gauge cock and check the siphon is clear and full of water. A dry
  siphon lets live steam reach the gauge mechanism and damages it.

- Read the pressure control settings. The burner must go to low fire at least 0.5 bar below the safety valve set
  pressure and stop at least 0.3 bar below it. Check the settings against the label on the control and the stamped
  set pressure on the safety valve body. Do not raise the control settings to reduce burner cycling.

- Check the boiler log for the times the valve lifted. If they match a large steam user shutting down, the burner is
  not reducing fire fast enough; check the modulating control and the low-fire switch.

- With the boiler at normal working pressure, listen at the safety valve outlet. A hiss or a wisp of steam below the
  set pressure means the seat is leaking and the valve must be removed for overhaul by a certified person.

**After the repair.** Record the gauge comparison, the control settings and the safety valve set pressure in the work
order and the boiler log. The steam engineer should watch the next start-up and the next load change before the job
is closed.

## Alarms

### E14 Low water level

**Meaning on KB-series boilers:** the second low-water probe detected water below the safe level and the burner was
locked out. Do not add feed water to a hot boiler that has run dry: allow it to cool and call the manufacturer.

**What the operator sees.** The burner stops and the alarm horn sounds. The lockout does not reset until the water is
back above the probe and the reset button is pressed. If the gauge glass shows no water at all, treat the boiler as
having run dry.

The first low-water probe should have taken the burner to low fire and sounded a warning before E14. If E14 arrived
without that warning, the first probe is faulty or fouled, and that is a serious fault in its own right: the boiler
must not be fired until both probes have been tested.

Checks, only with the steam engineer present:

- If water is visible in the gauge glass, blow down the gauge glass and confirm the level is real.

- Find out why the level fell: see Feed-water pump loses prime, and check the feed valve and the level controller.

- With the boiler cold and at 0 bar, remove and clean both probes. Scale on the probe insulator can make a dry probe
  read wet.

- Test the low-water lockout by blowing down the probe chamber with the burner running at low fire. The burner must
  lock out each time. Record the test in the boiler log.

### E31 Flame failure

The burner did not detect a flame within the safety time. Check the fuel supply valve is open, the oil filter is clean
and the ignition electrode gap is 3 mm. Reset once only; a second failure needs a qualified burner technician.

**What the operator sees.** The burner starts its pre-purge, tries to ignite, and locks out with E31 a few seconds
later. Sometimes the burner lights and then drops out after a minute, which also shows as E31.

Typical causes, most likely first:

1. Fuel supply: a closed or partly closed valve, a blocked oil filter, or water in the oil.
2. Ignition: electrode gap wrong, electrode insulator cracked, or carbon on the electrodes.
3. Flame detector (photocell) dirty or faulty, so a real flame is not seen.
4. Oil nozzle worn or partly blocked, giving a poor spray.

Checks:

- Confirm every valve on the fuel line from the day tank to the burner is open. Drain a sample from the day tank drain
  into a clear jar; water collects at the bottom and needs draining from the tank.

- With the burner isolated, clean or replace the oil filter element.

- Remove the ignition assembly. Clean the electrodes, check the insulators for cracks and set the electrode gap to
  3 mm.

- Remove the photocell and wipe its window clean. A soot film is enough to blind it.

- If the burner still fails, the nozzle, pump pressure and combustion settings need a qualified burner technician.
  Do not keep resetting the lockout: unburnt oil collects in the furnace with each attempt.
