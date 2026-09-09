"""Walkable Rune shelters. No terrain edits or scaled pieces."""
from pathlib import Path
import math
import json
import sys

project = Path(__file__).resolve().parents[1]
root = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else (project / 'blueprint-library' if (project / 'blueprint-library').is_dir() else project.parents[1] / 'outputs' / 'RuneCompanion' / 'blueprint-library')
root.mkdir(parents=True, exist_ok=True)

def entry(prefab, x, y, z, yaw=0):
    angle = math.radians(yaw / 2)
    values = [prefab, 'BuildingWorkbench', x, y, z, 0, math.sin(angle), 0, math.cos(angle), '', 1, 1, 1]
    return ';'.join(format(v, '.8g') if isinstance(v, (int, float)) else v for v in values)

def shelter(wall):
    # Furniture stays against the sides, leaving a two metre central aisle.
    pieces = [entry('piece_workbench', -1.95, 0, 3.8, 90),
              entry('bed', 1.9, 0, 3.8), entry('fire_pit', 4.9, 0, 3.8)]
    solid = wall != 'woodwall'
    stone = wall == 'stone_wall_4x2'
    depth = 8 if stone else 6
    side = 3.4 if solid else 3
    back = depth + (.3 if solid and not stone else 0)
    front = -.3 if solid and not stone else 0
    heights = [.5, 1.5] if wall == 'blackmarble_2x1x1' else [1]
    for y in heights:
        for z in ([2, 6] if stone else [1, 3, 5]):
            pieces += [entry(wall, -side, y, z, 90), entry(wall, side, y, z, 90)]
        for x in [-2, 0, 2]:
            pieces.append(entry('woodwall' if stone else wall, x, y, back))
        for x in [-2, 2]:
            pieces.append(entry('woodwall' if stone else wall, x, y, front))
    for z in range(1, depth, 2):
        pieces += [entry('wood_roof', -2, 2, z, -90),
                   entry('wood_roof', 2, 2, z, 90),
                   entry('wood_roof_top', 0, 3, z, 90)]
    # Gable closures; the centre of the front remains an unobstructed doorway.
    for z in [front, back]:
        pieces += [entry('wood_wall_roof', -2, 2, z),
                   entry('wood_wall_roof', 2, 2, z, 180),
                   entry('wood_wall_roof_top', 0, 3, z)]
    return pieces

designs = [
    ('Camp Shelter', 'Early game', 'woodwall', 'Wooden shelter.'),
    ('Stone Shelter', 'Mid game', 'stone_wall_4x2', 'Stone side walls and timber ends; needs a stonecutter already in range.'),
    ('Marble Shelter', 'Late game', 'blackmarble_2x1x1', 'Black marble shelter; needs a stonecutter already in range.'),
]
catalog = []
for name, stage, wall, description in designs:
    description += ' Workbench and bed inside, campfire outside, clear central aisle and open entrance. Prepare level ground.'
    pieces = shelter(wall)
    lines = [f'#Name:{name}', '#Creator:Rune Fellowship', f'#Description:{stage}. {description}', '#Category:Rune Shelters', '#Pieces'] + pieces
    filename = name.replace(' ', '_') + '.blueprint'
    root.joinpath(filename).write_text('\n'.join(lines) + '\n', encoding='utf-8')
    catalog.append(dict(name=name, stage=stage, file=filename, pieces=len(pieces), footprint='6 x 8 m interior plot; allow 11 x 12 m including fire and access' if wall == 'stone_wall_4x2' else '6 x 6 m interior plot; allow 11 x 10 m including fire and access', command=f'planbuild {name.lower()} on me', description=description))
root.joinpath('catalog.json').write_text(json.dumps(catalog, indent=2) + '\n', encoding='utf-8')
print(json.dumps(catalog, indent=2))
