"""Summarise a Unity test-results XML: one line per test case with its result, time and first failure line.
    uv run --no-project python Tools/test_report.py <results.xml>"""
import sys
import xml.etree.ElementTree as ET

root = ET.parse(sys.argv[1]).getroot()
for tc in root.iter('test-case'):
    res = tc.get('result')
    line = f"{tc.get('name'):28s} {res:8s} {float(tc.get('duration', 0)):7.1f}s"
    if res != 'Passed':
        msg = tc.find('failure/message')
        if msg is not None and msg.text:
            line += '  ' + msg.text.strip().splitlines()[0][:220]
    print(line)
