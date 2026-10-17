# airfoil-generator

Onshape FeatureScript that lofts a wing from calculated NACA airfoil sections.

- [airfoil_wing.fs](airfoil_wing.fs): the feature. Paste into an Onshape Feature Studio.
- [tools/naca_reference.py](tools/naca_reference.py): Python mirror of the airfoil math, used to check
  results against UIUC coordinate files.

```
python3 -I tools/naca_reference.py <dir containing UIUC .dat files>
```

Stations are given as NACA designations (4-digit, standard 5-digit), with optional per-station overrides, chord,
origin %, angle of attack and offsets; tips can be flat, rounded, elliptical or raked.

Status: tested in Onshape; built sections match UIUC NACA 2412/4415/23012/0015 coordinates to about 0.0001 chord.
