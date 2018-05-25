if (typeof Storage === 'undefined')
    window.alert('Please use a modern browser to properly view ASF GUI!');

function get(name) {
    return localStorage.getItem(name);
}

function store(name, val) {
    localStorage.setItem(name, val);
}