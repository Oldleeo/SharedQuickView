const targetIpInput = document.querySelector('#target-ip');
const sourcePathsInput = document.querySelector('#source-paths');
const transformButton = document.querySelector('#transform-button');
const resultBox = document.querySelector('#demo-result');
const resultPaths = document.querySelector('#result-paths');
const errorBox = document.querySelector('#demo-error');
const copyButton = document.querySelector('#copy-result');

const ipv4Pattern = /^(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)$/;
const protocolUrlPattern = /\b[a-z][a-z0-9+.-]{1,31}:\/\/[^\s<>"“”‘’']+/gi;
const uncPattern = /\\{2,}(?<host>[^\\\s]+)(?<tail>\\.*)$/;

function transformPaths() {
  const targetIp = targetIpInput.value.trim();
  if (!ipv4Pattern.test(targetIp)) {
    showError('请输入正确的 IPv4 地址，例如 192.168.1.100。');
    return;
  }

  const seen = new Set();
  const results = [];
  const normalized = sourcePathsInput.value
    .replace(/<br\s*\/?>/gi, '\n')
    .replace(/&#x20;|&nbsp;/gi, ' ')
    .replaceAll('＼', '\\')
    .replaceAll('\r', '');

  normalized.split('\n').forEach((rawLine) => {
    let line = rawLine.trim();
    if (!line || line === '\\') return;
    line = line.replace(protocolUrlPattern, ' ').trim().replaceAll('/', '\\');
    const match = line.match(uncPattern);
    if (!match?.groups?.tail) return;

    let tail = match.groups.tail.trim().replace(/["'”’]+$/g, '').trimEnd();
    tail = tail.replace(/\\{2,}/g, '\\');
    if (tail.length < 2) return;

    const converted = `\\\\${targetIp}${tail}`;
    const key = converted.toLocaleLowerCase();
    if (!seen.has(key)) {
      seen.add(key);
      results.push(converted);
    }
  });

  if (!results.length) {
    showError('没有识别到有效的 UNC 共享路径。');
    return;
  }

  errorBox.hidden = true;
  resultPaths.textContent = results.join('\n');
  resultBox.hidden = false;
  transformButton.firstChild.textContent = `已识别 ${results.length} 条 `;
}

function showError(message) {
  resultBox.hidden = true;
  errorBox.textContent = message;
  errorBox.hidden = false;
}

transformButton?.addEventListener('click', transformPaths);

copyButton?.addEventListener('click', async () => {
  try {
    await navigator.clipboard.writeText(resultPaths.textContent);
    copyButton.textContent = '已复制';
    setTimeout(() => { copyButton.textContent = '复制结果'; }, 1400);
  } catch {
    const selection = window.getSelection();
    const range = document.createRange();
    range.selectNodeContents(resultPaths);
    selection.removeAllRanges();
    selection.addRange(range);
    copyButton.textContent = '请按 Ctrl + C';
  }
});

const revealObserver = new IntersectionObserver((entries) => {
  entries.forEach((entry) => {
    if (entry.isIntersecting) {
      entry.target.classList.add('visible');
      revealObserver.unobserve(entry.target);
    }
  });
}, { threshold: 0.12 });

document.querySelectorAll('.reveal').forEach((element, index) => {
  element.style.transitionDelay = `${Math.min(index % 4, 3) * 60}ms`;
  revealObserver.observe(element);
});

const lightbox = document.querySelector('#lightbox');
const lightboxImage = document.querySelector('#lightbox-image');

document.querySelectorAll('.shot').forEach((button) => {
  button.addEventListener('click', () => {
    lightboxImage.src = button.dataset.image;
    lightboxImage.alt = button.dataset.alt;
    lightbox.showModal();
  });
});

document.querySelector('.lightbox-close')?.addEventListener('click', () => lightbox.close());
lightbox?.addEventListener('click', (event) => {
  if (event.target === lightbox) lightbox.close();
});
