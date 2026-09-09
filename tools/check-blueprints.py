"""Static file checks; native construction is verified separately in BlueprintLibrarySmoke."""
from pathlib import Path
import math
import json
import sys

project = Path(__file__).resolve().parents[1]
root = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else (project / 'blueprint-library' if (project / 'blueprint-library').is_dir() else project.parents[1] / 'outputs' / 'RuneCompanion' / 'blueprint-library')
catalog = json.loads(root.joinpath('catalog.json').read_text(encoding='utf-8'))
for design in catalog:
    lines = root.joinpath(design['file']).read_text(encoding='utf-8').splitlines()
    assert lines[0] == '#Name:' + design['name']
    assert '#Terrain' not in lines
    pieces = lines[lines.index('#Pieces') + 1:]
    assert 1 <= len(pieces) == design['pieces'] <= 256
    assert len(set(pieces)) == len(pieces)
    max_offset = 0
    for line in pieces:
        row = line.split(';')
        assert len(row) == 13, line
        xyz = [float(v) for v in row[2:5]]
        q = [float(v) for v in row[5:9]]
        assert all(math.isfinite(v) for v in xyz + q)
        assert abs(sum(v*v for v in q) - 1) < 0.00001
        assert row[10:13] == ['1', '1', '1']
        assert 0 <= xyz[1] <= 3
        max_offset = max(max_offset, math.sqrt(sum(v*v for v in xyz)))
    assert max_offset < 40
    prefabs = [line.split(';')[0] for line in pieces]
    assert all(prefabs.count(p) == 1 for p in ['piece_workbench', 'bed', 'fire_pit'])
    assert 'forge' not in prefabs and 'blackforge' not in prefabs
    print(f"PASS {design['name']}: {len(pieces)} pieces, {max_offset:.2f} m maximum offset, finite positions/rotations, normal scale, no terrain edits, no duplicate entries.")
