"""Extract existing GL2000 icon resources without executing or modifying the legacy app.
Usage: python Tools/Extract-Gl2000Icons.py C:\\Sistemas\\Fussion\\gl.exe
Requires Pillow. Output is the MAUI project's Resources/Images folder.
"""
import io
import struct
import sys
from pathlib import Path
from PIL import Image

data = Path(sys.argv[1]).read_bytes()
u16 = lambda p: struct.unpack_from('<H', data, p)[0]
u32 = lambda p: struct.unpack_from('<I', data, p)[0]
pe = u32(60)
optional = pe + 24
section_table = optional + u16(pe + 20)
sections = []
for i in range(u16(pe + 6)):
    p = section_table + i * 40
    sections.append((u32(p + 12), max(u32(p + 8), u32(p + 16)), u32(p + 20)))

def offset(rva):
    for address, size, pointer in sections:
        if address <= rva < address + size:
            return pointer + rva - address
    raise ValueError(f'Invalid resource address: {rva}')

root = offset(u32(optional + (112 if u16(optional) == 0x10b else 128)))
resources = {}

def visit(relative, path):
    p = root + relative
    for i in range(u16(p + 12) + u16(p + 14)):
        name, target = struct.unpack_from('<II', data, p + 16 + i * 8)
        if name & 0x80000000:
            q = root + (name & 0x7fffffff)
            name = data[q + 2:q + 2 + u16(q) * 2].decode('utf-16le')
        if target & 0x80000000:
            visit(target & 0x7fffffff, path + [name])
        else:
            q = root + target
            start = offset(u32(q))
            resources[tuple(path + [name])] = data[start:start + u32(q + 4)]

visit(0, [])
names = {'NOTE04_ICO': 'gl_catalog', 'NOTE07_ICO': 'gl_journals',
         'TRFFC09_ICO': 'gl_accumulate', 'QUERY3_ICO': 'gl_query',
         'BALANZA_ICO': 'gl_trial', 'NOTE06_ICO': 'gl_balances',
         'GL_ICO': 'gl_ledger', 'CANCELAR_ICO': 'gl_exit',
         'A_ICO': 'gl_add', 'C_ICO': 'gl_change', 'B_ICO': 'gl_delete',
         'FINDB_ICO': 'gl_search', 'QKQBE_ICO': 'gl_filter', 'WIZHELP_ICO': 'gl_help',
         'SALIR_ICO': 'gl_close', 'DROPS_ICO': 'gl_policy', 'ACEPTAR_ICO': 'gl_accept'}
destination = Path(__file__).resolve().parents[1] / 'GestionLibros/Resources/Images'
for key, group in resources.items():
    name = str(key[1]).replace('\\', '/').rsplit('/', 1)[-1]
    if key[0] != 14 or name not in names:
        continue
    count = struct.unpack_from('<H', group, 4)[0]
    entries, payloads = [], []
    position = 6 + 16 * count
    for i in range(count):
        entry = group[6 + 14*i:20 + 14*i]
        icon_id = struct.unpack_from('<H', entry, 12)[0]
        payload = next(v for k, v in resources.items() if k[0] == 3 and k[1] == icon_id)
        entries.append(entry[:8] + struct.pack('<II', len(payload), position))
        payloads.append(payload)
        position += len(payload)
    icon = Image.open(io.BytesIO(group[:6] + b''.join(entries) + b''.join(payloads)))
    icon.convert('RGBA').save(destination / (names[name] + '.png'))
    print(names[name], icon.size)
