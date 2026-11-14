#!/usr/bin/env python3
"""
Regenerates ENTITIES_COMPLETED.md: every entity classname Garry's Mod's server registers,
with GMod's datadesc (keyvalues/inputs/outputs/think funcs) and methods as sub-checkboxes,
auto-checked against what exists in Game.Server / Game.Shared.

Requirements: Python 3.10+, and binutils `nm` + `objdump` on PATH (e.g. msys2).
    python Utils/Scripts/gen_entities_checklist.py --bin <path/to/garrysmod/bin/server_srv.so> --obsoletium <obsolete-source-engine dir>

nm/objdump output is cached in the system temp dir, keyed by the binary's size + mtime.

To keep a box's state across regenerations (e.g. unchecking something that only exists as a
stub), append `<!-- manual -->` to that line. Its state is then copied over as-is.
"""
import argparse, collections, glob, hashlib, json, os, re, struct, subprocess, sys, tempfile

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
ap = argparse.ArgumentParser()
ap.add_argument('--bin', required=True, help="Garry's Mod Linux dedicated server binary (garrysmod/bin/server_srv.so)")
ap.add_argument('--obsoletium', required=True, help='Obsoletium source root (contains game/server and game/shared)')
ap.add_argument('--out', default=os.path.join(REPO, 'ENTITIES_COMPLETED.md'), help='output file (default: ENTITIES_COMPLETED.md at the repo root)')
args = ap.parse_args()

# ---------------------------------------------------------------------------------------------
# Binary: symbols + disassembly
# ---------------------------------------------------------------------------------------------
st_ = os.stat(args.bin)
key = hashlib.sha1(f'{os.path.abspath(args.bin)}|{st_.st_size}|{st_.st_mtime_ns}'.encode()).hexdigest()[:12]
cachedir = os.path.join(tempfile.gettempdir(), 'sourcenet_entity_checklist')
os.makedirs(cachedir, exist_ok=True)
def cached(name, cmd):
    path = os.path.join(cachedir, f'{key}.{name}.txt')
    if not os.path.exists(path):
        print(f'running {cmd[0]} (cached afterwards)...', file=sys.stderr)
        with open(path + '.tmp', 'wb') as f: subprocess.run(cmd, stdout=f, check=True)
        os.replace(path + '.tmp', path)
    return path
nm_path = cached('nm', ['nm', '-C', args.bin])
dis_path = cached('objdump', ['objdump', '-d', '--no-show-raw-insn', args.bin])

data = open(args.bin, 'rb').read()
e_shoff = struct.unpack_from('<I', data, 0x20)[0]
e_shentsize, e_shnum = struct.unpack_from('<HH', data, 0x2e)
secs = []
for i in range(e_shnum):
    _, typ, _, addr, off, size = struct.unpack_from('<IIIIII', data, e_shoff + i * e_shentsize)
    if addr: secs.append((addr, off if typ != 8 else None, size))  # 8 = SHT_NOBITS (.bss)
def rd(va, n):
    for a, o, s in secs:
        if a <= va < a + s: return data[o + va - a:o + va - a + n] if o is not None else bytes(n)
    return None
def u32(va):
    b = rd(va, 4); return struct.unpack('<I', b)[0] if b else None
def cstr(va):
    b = rd(va, 256) if va else None
    return b.split(b'\0')[0].decode('latin1') if b else None

factory_vt = {}; factory_ctor = {}; staticobj = {}; datamaps = {}; dmi = {}
methods = collections.defaultdict(set); netcls = set()
EXCL = re.compile(r'^(~|operator|GetDataDescMap|GetServerClass|GetBaseMap|YouForgotToImplement|GetSendTable|DataMapInit|NetworkStateChanged|GetPredDescMap|SendProxy|NetworkVar|GetClassName|m_|s_|GetEntitySize|Create$|Destroy$|ThisClass|BaseClass|GetPredictionDescMap|InternalGetDataDesc|PostConstructor$)')
for l in open(nm_path, encoding='utf-8', errors='ignore'):
    p = l.rstrip('\n').split(' ', 2)
    if len(p) < 3 or not p[0]: continue
    a, t, n = int(p[0], 16), p[1], p[2]
    if m := re.match(r'vtable for CEntityFactory<(.+)>$', n): factory_vt[a + 8] = m.group(1).strip(); continue
    if m := re.match(r'CEntityFactory<(.+)>::CEntityFactory\(char const\*\)$', n): factory_ctor[a] = m.group(1).strip(); continue
    if m := re.match(r'datamap_t\* DataMapInit<(.+)>\((.+)\*\)$', n): dmi[a] = m.group(1); continue
    if m := re.match(r'(.+)::m_DataMap$', n): datamaps[a] = m.group(1)
    if t in 'bBdD': staticobj.setdefault(a, n)
    if t in 'tTW' and (m := re.match(r'^([A-Za-z_][\w<>, ]*?)::(\w+)\(', n)):
        c, f = m.group(1), m.group(2)
        if f == 'GetServerClass': netcls.add(c)
        if f != c and not EXCL.match(f) and 'NetworkVar_' not in n: methods[c].add(f)

# LINK_ENTITY_TO_CLASS(name, T) = `static CEntityFactory<T> name("name")`, so the static object's
# symbol is the classname, and its constructor stores CEntityFactory<T>'s vtable into it (or calls
# the out-of-line ctor with it as `this`).
# DataMapInit<T> fills T::m_DataMap at runtime, and builds think/touch function names byte by byte.
ent2cls = {}; dd_raw = {}
regs = {}; lastesp = None; cur = None; store = {}; built = []; strbuf = {}; sregs = {}
SUBREG = {'si': 'esi', 'di': 'edi', 'bx': 'ebx', 'cx': 'ecx', 'dx': 'edx', 'cl': 'ecx', 'dl': 'edx', 'bl': 'ebx'}
def flush():
    if cur is not None: dd_raw[cur] = (dict(store), list(built))
for l in open(dis_path, encoding='utf-8', errors='ignore'):
    if l[:1] in '0123456789abcdef' and (m := re.match(r'^([0-9a-f]{8}) <', l)):
        flush(); cur = dmi.get(int(m.group(1), 16)); store = {}; built = []; strbuf = {}; sregs = {}
        continue
    s = l.split('\t')[-1].strip()
    # --- factories
    if r := re.match(r'movl\s+\$0x([0-9a-f]+),\(%esp\)$', s): lastesp = int(r.group(1), 16)
    elif (r := re.match(r'call\s+([0-9a-f]+) <', s)) and int(r.group(1), 16) in factory_ctor and lastesp in staticobj:
        ent2cls[staticobj[lastesp]] = factory_ctor[int(r.group(1), 16)]
    elif r := re.match(r'mov\s+\$0x([0-9a-f]+),(%e[a-z]+)$', s): regs[r.group(2)] = int(r.group(1), 16)
    elif (r := re.match(r'mov\s+(%e[a-z]+),0x([0-9a-f]+)$', s)) and r.group(1) in regs:
        v, o = regs[r.group(1)], int(r.group(2), 16)
        if v in factory_vt and o in staticobj: ent2cls[staticobj[o]] = factory_vt[v]
    elif r := re.match(r'movl\s+\$0x([0-9a-f]+),0x([0-9a-f]+)$', s):
        v, o = int(r.group(1), 16), int(r.group(2), 16)
        if v in factory_vt and o in staticobj: ent2cls[staticobj[o]] = factory_vt[v]
    # --- datadesc
    if cur is None: continue
    if m := re.match(r'movl\s+\$0x([0-9a-f]+),0x([0-9a-f]+)$', s): store[int(m.group(2), 16)] = int(m.group(1), 16); continue
    if m := re.match(r'mov\s+\$0x([0-9a-f]+),%(e..)$', s): sregs[m.group(2)] = int(m.group(1), 16); continue
    if m := re.match(r'mov\s+%([a-z]+),(?:0x([0-9a-f]+))?\(%eax\)$', s):
        r = m.group(1); full = SUBREG.get(r, r)
        if full in sregs:
            sz = 4 if r.startswith('e') else 2 if r[1] in 'xi' else 1
            strbuf[int(m.group(2) or '0', 16)] = (sregs[full] & ((1 << (8 * sz)) - 1)).to_bytes(sz, 'little')
        continue
    if m := re.match(r'mov([lwb])\s+\$0x([0-9a-f]+),(?:0x([0-9a-f]+))?\(%eax\)$', s):
        sz = {'l': 4, 'w': 2, 'b': 1}[m.group(1)]
        strbuf[int(m.group(3) or '0', 16)] = int(m.group(2), 16).to_bytes(sz, 'little'); continue
    if (m := re.match(r'mov\s+%ebx,0x([0-9a-f]+)$', s)) and strbuf:
        b = bytearray(64)
        for o, v in strbuf.items(): b[o:o + len(v)] = v
        built.append((int(m.group(1), 16), bytes(b).split(b'\0')[0].decode('latin1'))); strbuf = {}
flush()

# typedescription_t (32-bit): fieldType@0 fieldName@4 flags@18 externalName@20, 56 bytes
FTYPEDESC_KEY, FTYPEDESC_INPUT, FTYPEDESC_OUTPUT, FTYPEDESC_FUNCTIONTABLE = 4, 8, 16, 32
dd = {}
for cls, (st, built) in dd_raw.items():
    cands = [(m, st[m], st[m + 4]) for m in st if m + 4 in st and st[m + 4] < 5000 and datamaps.get(m) == cls]
    if not cands: dd[cls] = {'base': None, 'fields': []}; continue
    M, desc, n = cands[0]
    names = dict(built); fields = []
    for i in range(n):
        ea = desc + i * 56
        if u32(ea) == 0 and not u32(ea + 4) and (ea + 4) not in names: continue
        fields.append({'name': names.get(ea + 4) or cstr(u32(ea + 4)),
                       'flags': struct.unpack('<H', rd(ea + 18, 2))[0], 'ext': cstr(u32(ea + 20))})
    dd[cls] = {'base': datamaps.get(st.get(M + 12)), 'fields': fields}

ents = sorted(ent2cls.items())
missing = set(factory_vt.values()) - set(ent2cls.values())
if missing: print('warning: factories with no classname found:', sorted(missing), file=sys.stderr)

# ---------------------------------------------------------------------------------------------
# Obsoletium: reference source files
# ---------------------------------------------------------------------------------------------
OBS = args.obsoletium
obsfile = {}; obsname = {}; namefile = {}; ddsrc = {}
def prio(rel):
    return 0 if '/hl2mp/' in rel or '/gmod/' in rel else 1 if '/hl2/' in rel or '/episodic/' in rel else 2 if rel.count('/') == 2 else 3 if '/hl1/' in rel else 9
for f in sorted(glob.glob(f'{OBS}/game/server/**/*.cpp', recursive=True) + glob.glob(f'{OBS}/game/shared/**/*.cpp', recursive=True)):
    t = open(f, encoding='latin1').read(); rel = os.path.relpath(f, OBS).replace('\\', '/')
    for m in re.finditer(r'^\s*LINK_ENTITY_TO_CLASS\s*\(\s*(\w+)\s*,\s*(\w+)', t, re.M):
        n, c = m.groups()
        obsfile.setdefault(c, rel); obsname.setdefault(c, set()).add(n); namefile.setdefault((n, c), rel)
    for m in re.finditer(r'BEGIN_(?:DATADESC|SIMPLE_DATADESC)\s*\(\s*(\w+)\s*\)', t):
        if m.group(1) not in ddsrc or prio(rel) < ddsrc[m.group(1)][0]: ddsrc[m.group(1)] = (prio(rel), rel)
def find_src(c): return ddsrc[c][1] if c in ddsrc else obsfile.get(c)

dtdone = {}
for l in open(os.path.join(REPO, 'DATATABLES_COMPLETED.md'), encoding='utf-8'):
    if m := re.match(r'- \[(.)\] Class #\d+: (\w+)', l): dtdone[m.group(2)] = m.group(1) == 'x'

# ---------------------------------------------------------------------------------------------
# C#: classes, links, methods, string literals
# ---------------------------------------------------------------------------------------------
cs = {}; netname = {}
DECL = re.compile(r'^\s*(?:\[[^\]]*\]\s*)*(?:(?:public|private|protected|internal|static|override|virtual|abstract|new|sealed|unsafe|extern|partial|async|readonly|ref)\s+)*[\w<>\[\],.?]+(?:<[^()]*>)?[?]?\s+(\w+)\s*(?:<[^()]*>)?\s*\(')
CLS = re.compile(r'\b(?:class|struct)\s+(\w+)')
NOTMETHOD = {'if', 'while', 'for', 'foreach', 'switch', 'return', 'using', 'lock', 'catch', 'new', 'class', 'struct'}
def preprocess(raw):
    # Server view of a shared file: drop CLIENT_DLL-only branches, keep everything else.
    out = []; st = []
    for l in raw.split('\n'):
        t = l.strip()
        if t.startswith('#if'):
            cond = re.sub(r'\s+', '', t[3:])
            st.append('skip' if cond == 'CLIENT_DLL' else 'keep_then_skip' if cond == '!CLIENT_DLL' else 'both')
            out.append(''); continue
        if t.startswith('#elif') or t.startswith('#else'):
            if st: st[-1] = {'skip': 'keep', 'keep_then_skip': 'skip', 'both': 'both', 'keep': 'skip'}[st[-1]]
            out.append(''); continue
        if t.startswith('#endif'):
            if st: st.pop()
            out.append(''); continue
        out.append('' if 'skip' in st else l)
    raw = '\n'.join(out)
    return re.sub(r'\b(class|struct)\s*\n(\s*\n)*\s*(\w+)', lambda m: m.group(1) + ' ' + m.group(3) + '\n' * m.group(0).count('\n'), raw)
for f in sorted(glob.glob(f'{REPO}/Game.Server/**/*.cs', recursive=True) + glob.glob(f'{REPO}/Game.Shared/**/*.cs', recursive=True)):
    fs = f.replace('\\', '/')
    if '/obj/' in fs or '/bin/' in fs: continue
    raw = open(f, encoding='utf-8', errors='ignore').read()
    raw = preprocess(re.sub(r'/\*.*?\*/', lambda m: '\n' * m.group(0).count('\n'), raw, flags=re.S))
    rel = os.path.relpath(f, REPO).replace('\\', '/')
    depth = 0; stack = []; pending = []; pendnet = []; curm = None
    for ln in raw.split('\n'):
        ln = re.sub(r'^((?:[^"/]|/(?!/)|"(?:\\.|[^"\\])*")*)//.*$', r'\1', ln)
        code = re.sub(r'"(?:\\.|[^"\\])*"', '""', ln)
        pendnet += re.findall(r'NetworkName\s*\(\s*"(\w+)"', ln)
        pending += re.findall(r'LinkEntityToClass(?:Attribute)?\s*\(\s*"(\w+)"', ln)
        cm = CLS.search(code)
        if cm and not re.search(r'\bnew\b|\bwhere\b|typeof|nameof', code[:cm.start()]):
            name = cm.group(1)
            d = cs.setdefault(name, {'file': rel, 'methods': {}, 'strings': set(), 'links': []})
            d['links'] += pending
            for nn in pendnet: netname.setdefault(nn, name)
            pending = []; pendnet = []
            stack.append((name, depth))
        elif code.strip() and not code.strip().startswith('['): pending = []; pendnet = []
        if stack:
            name, cd = stack[-1]; d = cs[name]
            for s in re.findall(r'"((?:\\.|[^"\\])*)"', ln): d['strings'].add(s.lower())
            if depth == cd + 1 and (mm := DECL.match(code)) and mm.group(1) not in NOTMETHOD:
                curm = (name, mm.group(1)); d['methods'].setdefault(mm.group(1), False)
            if curm and 'NotImplementedException' in code: cs[curm[0]]['methods'][curm[1]] = True  # stub
        depth += code.count('{') - code.count('}')
        while stack and depth <= stack[-1][1] and '}' in code: stack.pop()
cslink = {}
for n, d in cs.items():
    for l in d['links']: cslink.setdefault(l, []).append(n)
cs_lower = {k.lower(): k for k in cs}

def csclass(c, names=()):
    want = netname.get(c) or (c[1:] if c.startswith('C') else c)
    want = want if want in cs else cs_lower.get(want.lower(), want)
    for n in names:
        if n in cslink: return want if want in cslink[n] else cslink[n][-1]
    if want in cs: return want
    return c if c in cs else None
def has_method(cn, m): return bool(cs.get(cn)) and m in cs[cn]['methods'] and not cs[cn]['methods'][m]
def has_str(cn, s): return bool(cs.get(cn)) and bool(s) and s.lower() in cs[cn]['strings']
def box(b): return '[x]' if b else '[ ]'
def all_checked(lines): return all('[x]' in s for s in lines if s.lstrip().startswith('- ['))

def detail(c, cn, ind):
    out = []; allok = True
    f = dd.get(c, {'fields': []})['fields']
    kv = [x for x in f if x['flags'] & FTYPEDESC_KEY and not x['flags'] & (FTYPEDESC_INPUT | FTYPEDESC_OUTPUT) and x['ext']]
    inp = [x for x in f if x['flags'] & FTYPEDESC_INPUT and x['ext']]
    outp = [x for x in f if x['flags'] & FTYPEDESC_OUTPUT and x['ext']]
    fn = [x for x in f if x['flags'] & FTYPEDESC_FUNCTIONTABLE and x['name']]
    skip = {x['name'] for x in inp} | {x['name'] for x in fn}
    def sec(title, items):
        nonlocal allok
        if not items: return
        out.append(f'{ind}- {title}')
        seen = set()
        for label, ok in items:
            if label in seen: continue
            seen.add(label); allok &= ok; out.append(f'{ind}  - {box(ok)} {label}')
    sec('KeyValues', [(f"`{x['ext']}`", has_str(cn, x['ext'])) for x in kv])
    sec('Inputs', [(f"`{x['ext']}`" + (' (also keyvalue)' if x['flags'] & FTYPEDESC_KEY else ''),
                    has_str(cn, x['ext']) and (not x['name'].startswith('Input') or has_method(cn, x['name']))) for x in inp])
    sec('Outputs', [(f"`{x['ext']}`", has_str(cn, x['ext'])) for x in outp])
    sec('Think/Touch/Use functions', [(f"`{x['name']}`", has_method(cn, x['name'])) for x in fn])
    sec('Methods', [(f'`{m}`', has_method(cn, m)) for m in sorted(methods.get(c, ())) if m not in skip])
    return out, allok

# ---------------------------------------------------------------------------------------------
# Build
# ---------------------------------------------------------------------------------------------
byclass = collections.defaultdict(list)
for n, c in ents: byclass[c].append(n)
CANON = {'CPhysicsProp': 'prop_physics', 'CDynamicProp': 'prop_dynamic', 'CRagdollProp': 'prop_ragdoll', 'CLight': 'light',
         'CInfoTarget': 'info_target', 'CFuncBrush': 'func_brush', 'CSprite': 'env_sprite'}
canon = {}
for c, ns in byclass.items():
    pool = [n for n in ns if n in cslink] or [n for n in ns if n in obsname.get(c, ())] or ns
    canon[c] = CANON[c] if CANON.get(c) in ns else sorted(pool, key=lambda n: (len(n), n))[0]

def chain(c):
    b = dd.get(c, {}).get('base')
    while b: yield b; b = dd.get(b, {}).get('base')
allbases = {b for c in byclass for b in chain(c)}
BASEISH = {'CBaseEntity', 'CPointEntity', 'CBaseAnimating', 'CBaseToggle', 'CBaseTrigger', 'CBaseCombatCharacter', 'CAI_BaseNPC',
           'CBaseFlex', 'CBaseAnimatingOverlay', 'CBaseCombatWeapon', 'CBasePlayer', 'CBreakableProp', 'CBaseProp', 'CLogicalEntity',
           'CServerOnlyEntity', 'CServerOnlyPointEntity', 'CBaseGrenade'}
inbase = {c for c in byclass if c in allbases and c in BASEISH}

canonok = {}
for c, n in canon.items():
    cn = csclass(c) if c in inbase else csclass(c, [n] + byclass[c])
    _, ok = detail(c, cn, '')
    canonok[c] = ok and cn is not None and (c in inbase or n in cslink)

def csdesc(cn): return f'C# `{cn}` ([{cs[cn]["file"]}]({cs[cn]["file"]}))' if cn else 'C#: *none*'
groups = collections.defaultdict(list)
for n, c in ents: groups[n.split('_')[0] if '_' in n.strip('_') else n].append((n, c))
basesneeded = set(); body = []; done = 0
for g in sorted(groups):
    items = groups[g]
    body.append(f'\n## {g}_*\n' if len(items) > 1 or items[0][0] != g else f'\n## {g}\n')
    for n, c in items:
        cn = csclass(c, [n] + byclass[c]); linked = n in cslink
        src = namefile.get((n, c)) or find_src(c)
        sub = [f'  - {box(linked)} Linked (`{n}`)']
        want = csclass(c)
        if linked and want not in cslink[n]:
            sub.append(f'  - \u26a0 C# links `{n}` to `' + '`, `'.join(cslink[n]) + f'`, but GMod uses `{c}`')
        if c in netcls:
            sub.append(f'  - {box(bool(dtdone.get(c)) and cn is not None)} Networked (SendTable for `{c}`)')
        ok = True
        if c in inbase:
            sub.append(f'  - {box(canonok[c])} `{c}` functionality (tracked under Base classes)')
            basesneeded.add(c)
        elif canon[c] == n:
            d, ok = detail(c, cn, '  '); sub += d
            others = [x for x in byclass[c] if x != n]
            if others: sub.append('  - Also linked as: ' + ', '.join(f'`{x}`' for x in others))
        else:
            sub.append(f'  - {box(canonok[c])} Shares `{c}` with `{canon[c]}`, which tracks its functionality')
        allok = linked and ok and cn is not None and all_checked(sub)
        done += allok
        body.append(f'- {box(allok)} **{n}** · `{c}` · ' + (f'`{src}`' if src else '*GMod-only*') + f' · {csdesc(cn)}')
        body += sub
        basesneeded.update(chain(c))

bbody = ['\n## Base classes\n', 'Datadesc and methods that the entities above inherit.\n']
for c in sorted(b for b in basesneeded if b not in byclass or b in inbase):
    cn = csclass(c); d, ok = detail(c, cn, '  ')
    if c in netcls: d.insert(0, f'  - {box(bool(dtdone.get(c)) and cn is not None)} Networked (SendTable for `{c}`)')
    ok = ok and cn is not None and all_checked(d)
    base = dd.get(c, {}).get('base'); src = find_src(c)
    bbody.append(f'- {box(ok)} **{c}**' + (f' : `{base}`' if base else '') + ' · ' + (f'`{src}`' if src else '*GMod-only*') + f' · {csdesc(cn)}')
    bbody += d

lines = '\n'.join(body + bbody).split('\n')

# Carry over boxes marked <!-- manual --> from the previous file, keyed by (entry, line text).
MANUAL = '<!-- manual -->'
manual = {}
if os.path.exists(args.out):
    top = None
    for l in open(args.out, encoding='utf-8').read().split('\n'):
        if l.startswith('- ['): top = re.sub(r'^- \[.\] ', '', l).replace(MANUAL, '').strip()
        if MANUAL in l and (m := re.match(r'^(\s*- )\[(.)\] (.*)$', l)):
            manual[(top if not l.startswith('- [') else None, m.group(3).replace(MANUAL, '').strip())] = m.group(2)
if manual:
    top = None
    for i, l in enumerate(lines):
        if l.startswith('- ['): top = re.sub(r'^- \[.\] ', '', l).strip()
        if m := re.match(r'^(\s*- )\[(.)\] (.*)$', l):
            k = (top if not l.startswith('- [') else None, m.group(3).strip())
            if k in manual: lines[i] = f'{m.group(1)}[{manual[k]}] {m.group(3)} {MANUAL}'
    # Re-derive each entry's box (unless it is itself manual) and the progress count.
    done = 0; in_bases = False
    for i, l in enumerate(lines):
        if l.startswith('## '): in_bases = l.startswith('## Base classes')
        if not l.startswith('- ['): continue
        j = i + 1
        while j < len(lines) and lines[j].startswith('  '): j += 1
        if MANUAL not in l: l = lines[i] = f'- {box(all_checked(lines[i + 1:j]))}' + l[5:]
        done += not in_bases and l.startswith('- [x]')

# ---------------------------------------------------------------------------------------------
# Stats (computed from the final lines, so manual overrides count)
# ---------------------------------------------------------------------------------------------
entries = []; cur = None; family = None; in_bases = False; kind = None
for l in lines:
    if l.startswith('## '):
        in_bases = l.startswith('## Base classes'); family = l[3:].strip(); continue
    if l.startswith('- ['):
        name = re.match(r'- \[.\] \*\*(.+?)\*\*', l).group(1)
        m = re.search(r' · C# `(\w+)`', l)
        cur = {'name': name, 'base': in_bases, 'family': family, 'done': l.startswith('- [x]'),
               'cls': name if in_bases else re.search(r'\*\* · `([^`]+)`', l).group(1), 'cs': m.group(1) if m else None,
               'boxes': collections.Counter(), 'checked': collections.Counter()}
        entries.append(cur); kind = None; continue
    if cur is None: continue
    if m := re.match(r'^  - ([A-Z][\w/ ]+)$', l): kind = m.group(1); continue
    if m := re.match(r'^\s+- \[(.)\] (\S+)', l):
        k = kind if l.startswith('    ') else {'Linked': 'Linked', 'Networked': 'Networked'}.get(m.group(2), None)
        if k is None: continue
        cur['boxes'][k] += 1; cur['checked'][k] += m.group(1) == 'x'
for e in entries:
    e['total'] = sum(e['boxes'].values()); e['have'] = sum(e['checked'].values()); e['left'] = e['total'] - e['have']

def pct(a, b): return f'{100 * a / b:.1f}%' if b else '-'
def bar(a, b, w=20):
    n = round(w * a / b) if b else 0
    return '`' + '█' * n + '░' * (w - n) + '`'
def table(head, rows, right=()):
    rows = [[str(c) for c in r] for r in rows]
    w = [max(len(x) for x in col) for col in zip(head, *rows)]
    def fmt(r): return '| ' + ' | '.join(c.rjust(w[i]) if i in right else c.ljust(w[i]) for i, c in enumerate(r)) + ' |'
    sep = '| ' + ' | '.join('-' * (w[i] - 1) + ':' if i in right else '-' * w[i] for i in range(len(w))) + ' |'
    return [fmt(head), sep] + [fmt(r) for r in rows]

ents_only = [e for e in entries if not e['base']]
bases_only = [e for e in entries if e['base']]
all_boxes = sum(e['total'] for e in entries); all_have = sum(e['have'] for e in entries)
kinds = ['Linked', 'Networked', 'KeyValues', 'Inputs', 'Outputs', 'Think/Touch/Use functions', 'Methods']
ktot = collections.Counter(); khave = collections.Counter()
for e in entries: ktot.update(e['boxes']); khave.update(e['checked'])
has_cs = sum(1 for c in byclass if csclass(c))
not_started = sum(1 for e in ents_only if e['total'] and e['have'] == 0)

stats = ['## Progress', '',
         f'{bar(done, len(ents))} **{done} / {len(ents)} classnames complete ({pct(done, len(ents))})**', '',
         f'{bar(all_have, all_boxes)} **{all_have:,} / {all_boxes:,} boxes checked ({pct(all_have, all_boxes)})**', '',
         f'- {has_cs} / {len(byclass)} GMod C++ classes have a C# class ({pct(has_cs, len(byclass))})',
         f'- {sum(e["done"] for e in bases_only)} / {len(bases_only)} base classes complete',
         f'- {not_started} classnames have no boxes checked', '']
stats += table(['Kind', 'Checked', 'Total', 'Done'],
               [[k, f'{khave[k]:,}', f'{ktot[k]:,}', pct(khave[k], ktot[k])] for k in kinds if ktot[k]], right=(1, 2, 3))

def row(e): return [e['name'], f'{e["have"]}/{e["total"]}', pct(e['have'], e['total']), e['left']]
started = [e for e in entries if not e['done'] and e['have'] and e['total'] >= 4]
closest = sorted(started, key=lambda e: (-e['have'] / e['total'], e['left'], e['name']))[:10]
stats += ['', '### Closest to done', ''] + table(['Entry', 'Checked', 'Done', 'Left'], [row(e) for e in closest], right=(1, 2, 3))
furthest = sorted((e for e in entries if not e['done']), key=lambda e: (-e['left'], e['name']))[:10]
stats += ['', '### Most work left', ''] + table(['Entry', 'Checked', 'Done', 'Left'], [row(e) for e in furthest], right=(1, 2, 3))

fams = collections.defaultdict(list)
for e in ents_only: fams[e['family']].append(e)
famrows = []
for f, es in fams.items():
    if len(es) < 5: continue
    t = sum(e['total'] for e in es); h = sum(e['have'] for e in es)
    famrows.append((h / t if t else 0, [f, f'{sum(e["done"] for e in es)}/{len(es)}', f'{h:,}/{t:,}', pct(h, t)]))
stats += ['', '### By family (5+ classnames)', ''] + table(['Family', 'Complete', 'Boxes checked', 'Done'],
    [r for _, r in sorted(famrows, key=lambda x: (-x[0], x[1][0]))], right=(1, 2, 3))
stats_md = '\n'.join(stats) + '\n'

LETTERS = list(zip('LNKIOTM', kinds))
percls = {}
for e in entries:
    c = percls.setdefault(e['cls'], {'cs': None, 'names': [], 'boxes': collections.Counter(), 'checked': collections.Counter()})
    c['cs'] = c['cs'] or e['cs']
    if not e['base']: c['names'].append(e['name'])
    c['boxes'].update(e['boxes']); c['checked'].update(e['checked'])
summary = []
for cls, c in percls.items():
    t = sum(c['boxes'].values()); h = sum(c['checked'].values())
    flags = ' '.join(ch if c['boxes'][k] and c['checked'][k] == c['boxes'][k] else '_' if c['boxes'][k] else '-' for ch, k in LETTERS)
    disp = c['cs'] or (cls[1:] if re.match(r'C[A-Z]', cls) else cls)
    aka = f' (aka {", ".join(sorted(c["names"]))})' if c['names'] else ''
    summary.append((-(h / t if t else 0), -t, disp, f'{box(t > 0 and h == t)} [{flags}] {disp}{aka}'))
classes_md = '\n'.join(['', '## All classes', '',
    '`L N K I O T M` stands for Linked, Networked, KeyValues, Inputs, Outputs, Think/Touch/Use functions and Methods. '
    '`[x]` means the class is fully complete. A letter means every box of that kind is checked, `_` means some are not, and `-` means the class has none of that kind. '
    'Classes are sorted from most to least complete by share of boxes checked.', '', '```'] +
    [s for *_, s in sorted(summary)] + ['```']) + '\n'

hdr = f"""<!-- Generated by Utils/Scripts/gen_entities_checklist.py. Hand edits are overwritten on regeneration unless the line carries the manual marker described below. -->
This is a list of every entity classname that Garry's Mod's server (`server_srv.so`) registers, and how complete each one is in Source.NET.

- Classnames and their C++ classes come from the `CEntityFactory<T>` registrations in `server_srv.so` ({len(ents)} classnames, {len(byclass)} C++ classes).
- KeyValues, Inputs, Outputs and Think/Touch/Use functions come from GMod's own datadesc tables in `server_srv.so`, so they include GMod's changes.
- Methods are the member functions that GMod's binary defines for the class. Inlined methods may be missing, and input handlers are listed under Inputs instead.
- Reference paths point into Obsoletium. *GMod-only* means Obsoletium has no source for the class, so the binary is the only reference.
- Sub-boxes are auto-checked when the C# class has a matching keyvalue/input/output string, or a method of the same name that doesn't throw `NotImplementedException`. That only proves the member exists, not that it behaves like GMod. To override a box, set it by hand and end the line with `{MANUAL}`.
- An entity is checked only when every sub-box under it is checked. Inherited functionality is tracked once, under **Base classes** at the bottom.
- When several classnames share one C++ class, one of them lists the functionality and the others only track their link.
- \u26a0 marks a classname that C# links to a different class than GMod does.

{stats_md}"""
open(args.out, 'w', encoding='utf-8', newline='\n').write(hdr + '\n'.join(lines) + '\n' + classes_md)
print(f'{done} / {len(ents)} classnames complete, {len(byclass)} classes -> {args.out}', file=sys.stderr)
