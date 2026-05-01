# -*- coding: utf-8 -*-
f = u'd:\\someのWinUI3\\RouteStar\\RouteStar\\web\\index.html'
with open(f, 'r', encoding='utf-8') as fh:
    content = fh.read()

new_card = '''                <!-- 崩坏3 -->
                <div class="hoyo-drop-card" data-game-id="Hoyo_H3">
                    <div class="hoyo-drop-card-bg" style="background-image:url('http://launcher.local/icon/h3.png')"></div>
                    <div class="hoyo-drop-card-overlay"></div>
                    <div class="hoyo-drop-card-body">
                        <div class="hoyo-drop-card-name">崩坏3</div>
                        <div class="hoyo-drop-card-desc">终将到来的律者，终将落幕的终焉</div>
                        <button class="game-card-add-btn">添加</button>
                    </div>
                </div>
'''

marker = '                <!-- 崩坏：因缘精灵 -->'
replacement = new_card + '                <!-- 崩坏：因缘精灵 -->'

if marker in content:
    new_content = content.replace(marker, replacement, 1)
    with open(f, 'w', encoding='utf-8') as fh:
        fh.write(new_content)
    print('OK')
else:
    print('MARKER_NOT_FOUND')
