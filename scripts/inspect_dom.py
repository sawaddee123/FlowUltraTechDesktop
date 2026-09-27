from bs4 import BeautifulSoup

with open('extracted_sources/CaptchaJigsaw.html', 'r', encoding='utf-8', errors='ignore') as f:
    soup = BeautifulSoup(f.read(), 'html.parser')

img = soup.find('img', id='captcha-verify-image')
if img:
    print('Found captcha-verify-image!')
    curr = img
    for i in range(5):
        if curr.parent:
            curr = curr.parent
            print(f'Level {i+1}: <{curr.name} id="{curr.get("id")}" class="{curr.get("class")}" style="{curr.get("style")}">')
    
    l3 = img.find_parent('div', id='captcha-verify-container-main-page')
    if l3:
        # Strip large base64 so we can see the full DOM structure clearly
        html = l3.prettify()
        import re
        html = re.sub(r'data:image/[^;]+;base64,[^"\']+', 'data:image/...[base64]...', html)
        print(html)
