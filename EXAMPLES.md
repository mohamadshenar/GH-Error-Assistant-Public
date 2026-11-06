# Examples

## Missing Curve reference and Solver error

A checked Curve reference may be missing from the open Rhino file along a verified wire path to Solver. Open the intended Rhino file and inspect the parameter holding the reference. Reassign deliberately if needed, recompute and Check Again. The connection does not prove the missing reference caused Solver's exception; inspect remaining errors separately.

## Empty Number

For an unwired/unassigned Number, supply the intended value or a source that provides it. Recompute, inspect the actual value and Check Again. Item count alone does not prove the intended value is correct.

## Null samples and conversion warnings

Null samples are missing values, not missing Rhino references. Inspect where they enter the data. For conversion warnings, compare the source type, destination Help and original message. Do not invent a construction or cleanup operation from error wording alone.

## Data groups and replacement warnings

Different paths are not automatically wrong. Compare grouping with the operation's documented needs before changing it. If one warning disappears and another remains, explicitly investigate the current warning. Historical findings remain labeled and do not keep driving old repair instructions.
