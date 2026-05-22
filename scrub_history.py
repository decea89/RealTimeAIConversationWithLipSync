import os
import re

# This script replaces OPENAI key patterns starting with 'sk-proj-'
# inside text/serialized files. It is intended to be used by git filter-branch
# as a tree-filter. It writes files in-place when a replacement occurs.

pattern = re.compile(rb"sk-proj-[^\s\"']+")

for root, dirs, files in os.walk('.'):
    for name in files:
        if name.endswith(('.unity', '.prefab', '.asset', '.txt', '.json', '.md', '.cs')):
            path = os.path.join(root, name)
            try:
                with open(path, 'rb') as f:
                    data = f.read()
                new = pattern.sub(b'REDACTED_OPENAI_KEY', data)
                if new != data:
                    with open(path, 'wb') as f:
                        f.write(new)
            except Exception:
                # ignore files we cannot read/write
                pass
