# Unified Background Processing

## Taxonomy
The overall goal of UBP is to properly handle background resources without
directly having to simulate all ships in the background. The way we do this
is by modeling the resource flows on a ship in a way that can be (mostly)
analytically solved.

### Producer
These are parts that produce a resource for free. They may have conditions on
when they are capable of producing that resource and how much of that resource
they can produce.

This includes things like:
- Solar Panels
- RTGs (in stock, some mods may change this)
- Modded science experiments that produce science over time without consuming
  anything.

It notably does not include things like:
- Nuclear reactors (these consume, e.g., Enriched Uranium)
- Any resource harvester that consumes electricity.

### Converter
These are parts that take one or more resources and convert them into one or
more resources. For most use cases this should be a linear conversion
(X units/s of inputs always turns into Y units/s of outputs). In general, you'll
want to compute a steady-state rate and use that for background processing.
There are some exceptions to this to support things like science labs, which do
not have a linear processing rate.

This includes things like:
- Fuel Cells (convert LF + O to EC)
- Surface Drills (convert EC to Ore)
- Asteroid Drills (convert EC + Asteroid Mass to Ore)
- Nuclear Reactors (convert EnrU to EC + Depleted Uranium)
- ISRU Processors (convert EC + Ore to various resources)
- Science Labs (convert EC + Data to Science)

### Consumer
Parts which consume a resource. These are generally a constant load but may have
more complicated behaviours (e.g. boiloff unless there is sufficient power).

This includes things like:
- Probe Cores (consume EC)
- Most other parts which passively consume EC (e.g. antennas)
- Decay of RTGs/uranium (if added by mods)
- Hydrogen boiloff (consumes EC, otherwise consumes LH2)
