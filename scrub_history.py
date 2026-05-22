import os
import re

# This script replaces OpenAI key patterns starting with the project key
# prefix (prefix is obfuscated in source to avoid leaving the literal).
# Intended for use as a tree-filter by history-rewrite tools.

# Construct the prefix without including the contiguous literal in the file.
prefix = b''.join([b'sk', b'-', b'proj', b'-'])
pattern = re.compile(prefix + b"[^\\s\\\"']+")

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
