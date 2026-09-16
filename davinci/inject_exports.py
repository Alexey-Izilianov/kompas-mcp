# -*- coding: utf-8 -*-
# Добавляет native-экспорты в IL-файл (ildasm-вывод DavinciNative.dll).
# Начиная с ILAsm 2.0 достаточно .export [N] as NAME в каждом методе —
# vtfixup/vtentry ILAsm генерирует сам (Serge Lidin, Inside IL Assembler).
import sys

EXPORTS = ["LIBRARYID", "LIBRARYNAME", "LIBRARYNAMEW", "LIBRARYPROTECTNUMBER",
           "LibIsOnApplication7", "LibInterfaceNotifyEntry",
           "LibInterfaceNotifyDisconnect", "LIBRARYENTRY"]

src = sys.argv[1]
dst = sys.argv[2]

with open(src, "r", encoding="utf-8-sig") as f:
    lines = f.read().splitlines()

for n, name in enumerate(EXPORTS, 1):
    endmark = "} // end of method DavinciNative::" + name
    end_i = next(i for i, l in enumerate(lines) if l.strip() == endmark)
    brace = max(i for i in range(end_i) if lines[i].strip() == "{")
    lines[brace + 1:brace + 1] = ["    .export [%d] as %s" % (n, name)]

with open(dst, "w", encoding="utf-8") as f:
    f.write("\n".join(lines) + "\n")
print("OK: %d экспортов добавлено" % len(EXPORTS))