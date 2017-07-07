<script>
    import { each } from "lodash";

    const fieldComponents = {};
    const fields = require.context("../fields", false, /^\.\/([\w-_]+)\.vue$/);

    each(fields.keys(), key => {
        const name = key.replace(/^\.\//, "").replace(/\.vue/, "");
        fieldComponents[name] = fields(key);
    });

    export default {
        data () {
            return {
                model: {},
                displayAdvanced: false,
                selectedVersion: 'latest'
            }
        },
        methods: {
            updateModel(value, field) {
                this.model[field] = value;
            },
            downloadText(text, filename) {
                const element = document.createElement('a');
                element.setAttribute('href', 'data:text/plain;charset=utf-8,' + encodeURIComponent(text));
                element.setAttribute('download', filename);

                element.style.display = 'none';
                document.body.appendChild(element);

                element.click();

                document.body.removeChild(element);
            },
            toggleAdvanced() {
                this.displayAdvanced = !this.displayAdvanced;
            },
            validateForm() {
                const form = document.getElementsByTagName('form')[0];

                if (form.checkValidity()) return true;

                clearTimeout(this.shakeTimeout);
                const fields = document.getElementsByClassName('error');
                each(fields, field => { field.classList.add('shake'); });
                this.shakeTimeout = setTimeout(() => { each(fields, field => { field.classList.remove('shake'); }); }, 500);
                return false;
            }
        },
        components: fieldComponents
    }
</script>

<style lang="scss">
    .form-item:last-child {
        margin-bottom: 0;
    }
</style>
