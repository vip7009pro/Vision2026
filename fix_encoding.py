import os

def convert_to_utf8_bom(filepath):
    try:
        with open(filepath, 'rb') as f:
            raw = f.read()
            
        if raw.startswith(b'\xef\xbb\xbf'):
            return False # Already has BOM
            
        # Try decoding as utf-8
        try:
            text = raw.decode('utf-8')
            # It is valid utf-8. Let's write it back with BOM
            with open(filepath, 'w', encoding='utf-8-sig') as f:
                f.write(text)
            return True
        except UnicodeDecodeError:
            # Maybe it's ANSI (cp1252 or cp1258).
            # Let's try to decode with local encoding or cp1258 (Vietnamese)
            try:
                text = raw.decode('cp1258')
                with open(filepath, 'w', encoding='utf-8-sig') as f:
                    f.write(text)
                return True
            except UnicodeDecodeError:
                pass
    except Exception as e:
        print(f"Error processing {filepath}: {e}")
    return False

def process_directory(directory):
    count = 0
    for root, dirs, files in os.walk(directory):
        if 'bin' in root or 'obj' in root or '.git' in root:
            continue
        for file in files:
            if file.endswith('.cs') or file.endswith('.xaml'):
                filepath = os.path.join(root, file)
                if convert_to_utf8_bom(filepath):
                    print(f"Converted: {filepath}")
                    count += 1
    print(f"Total files converted to UTF-8 with BOM: {count}")

if __name__ == '__main__':
    process_directory(r'g:\NODEJS\Vision2026')
