import json
import sys
sys.stdout.reconfigure(encoding='utf-8')

log_file = r'C:\Users\vip70\.gemini\antigravity-ide\brain\328d7403-4ead-4ad5-a0fd-ebc17b5cc067\.system_generated\logs\transcript_full.jsonl'

try:
    with open(log_file, 'r', encoding='utf-8') as f:
        for line in f:
            try:
                data = json.loads(line)
                if data.get('type') == 'PLANNER_RESPONSE':
                    tool_calls = data.get('tool_calls', [])
                    for tc in tool_calls:
                        if tc.get('name') in ('default_api:replace_file_content', 'default_api:multi_replace_file_content'):
                            args = tc.get('arguments', {})
                            target = args.get('TargetFile', '').lower()
                            if 'class1.cs' in target:
                                print('FOUND: ' + target)
                                chunks = args.get('ReplacementChunks', [])
                                if chunks:
                                    for idx, c in enumerate(chunks):
                                        print('CHUNK START LINE: ' + str(c.get('StartLine')))
                                        print(c.get('ReplacementContent'))
                                        print('---END CHUNK---')
                                elif 'ReplacementContent' in args:
                                    print('REPLACEMENT START LINE: ' + str(args.get('StartLine')))
                                    print(args.get('ReplacementContent'))
                                    print('---END REPLACEMENT---')
            except Exception as e:
                pass
except Exception as e:
    print(e)
