// Images keep their skeleton shimmer (CSS: img:not(.loaded)) until they load or fail.
// load/error don't bubble, hence the capture listener on document. Loaded in <head> so it listens before any <img> exists.
for (const type of ["load", "error"])
    document.addEventListener(type, e => e.target instanceof HTMLImageElement && e.target.classList.add("loaded"), true);
