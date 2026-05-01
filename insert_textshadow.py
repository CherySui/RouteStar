# -*- coding: utf-8 -*-
f = u'd:\\someのWinUI3\\RouteStar\\RouteStar\\web\\style.css'

with open(f, 'r', encoding='utf-8', errors='replace') as fh:
    content = fh.read()

changes = 0

# 1. 把默认背景图写入 .global-bg-image CSS，让页面一加载就显示，不等 JS
old1 = '.global-bg-image {\n    background-size: cover;\n    background-position: center;\n}'
new1 = '.global-bg-image {\n    background-size: cover;\n    background-position: center;\n    /* 默认背景图，JS 载入后会按需覆盖 */\n    background-image: url(\'http://launcher.local/img/First_menu_bg.png\');\n}'
if old1 in content:
    content = content.replace(old1, new1, 1)
    changes += 1
    print('1. default bg in CSS: OK')
else:
    # try CRLF
    old1b = '.global-bg-image {\r\n    background-size: cover;\r\n    background-position: center;\r\n}'
    new1b = '.global-bg-image {\r\n    background-size: cover;\r\n    background-position: center;\r\n    /* 默认背景图，JS 载入后会按需覆盖 */\r\n    background-image: url(\'http://launcher.local/img/First_menu_bg.png\');\r\n}'
    if old1b in content:
        content = content.replace(old1b, new1b, 1)
        changes += 1
        print('1. default bg in CSS (CRLF): OK')
    else:
        print('1. global-bg-image NOT FOUND')

# 2. 调亮 .home-desc 文字颜色
old2 = '    color: var(--text-secondary);\n    margin: 8px 0 0 0;\n    text-shadow: 0 1px 4px rgba(0,0,0,0.5);'
new2 = '    color: rgba(255,255,255,0.82);\n    margin: 8px 0 0 0;\n    text-shadow: 0 1px 4px rgba(0,0,0,0.5);'
if old2 in content:
    content = content.replace(old2, new2, 1)
    changes += 1
    print('2. home-desc color brightened: OK')
else:
    old2b = '    color: var(--text-secondary);\r\n    margin: 8px 0 0 0;\r\n    text-shadow: 0 1px 4px rgba(0,0,0,0.5);'
    new2b = '    color: rgba(255,255,255,0.82);\r\n    margin: 8px 0 0 0;\r\n    text-shadow: 0 1px 4px rgba(0,0,0,0.5);'
    if old2b in content:
        content = content.replace(old2b, new2b, 1)
        changes += 1
        print('2. home-desc color brightened (CRLF): OK')
    else:
        print('2. home-desc NOT FOUND')

# 3. 同时调亮 tips-text（之前是 0.4 透明度，太暗了）
old3 = '    color: rgba(255,255,255,0.4);'
new3 = '    color: rgba(255,255,255,0.62);'
if old3 in content:
    content = content.replace(old3, new3, 1)
    changes += 1
    print('3. tips-text color brightened: OK')
else:
    print('3. tips-text NOT FOUND')

with open(f, 'w', encoding='utf-8') as fh:
    fh.write(content)
print(f'DONE ({changes} changes)')
