import os

def fix_double_newlines(filepath):
    try:
        with open(filepath, 'rb') as f:
            raw = f.read()
        
        # Replace \r\r\n with \r\n
        if b'\r\r\n' in raw:
            fixed = raw.replace(b'\r\r\n', b'\r\n')
            with open(filepath, 'wb') as f:
                f.write(fixed)
            return True
    except Exception as e:
        pass
    return False

def process_directory(directory):
    count = 0
    for root, dirs, files in os.walk(directory):
        if 'bin' in root or 'obj' in root or '.git' in root:
            continue
        for file in files:
            if file.endswith('.cs') or file.endswith('.xaml'):
                filepath = os.path.join(root, file)
                if fix_double_newlines(filepath):
                    count += 1
    print(f"Fixed double newlines in {count} files")

if __name__ == '__main__':
    process_directory(r'g:\NODEJS\Vision2026')
