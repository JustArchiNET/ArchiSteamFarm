<script>
  import { each } from 'lodash';

  import schema from '../../schema';

  const fieldComponents = {};
  const fields = require.context('../fields', false, /^\.\/([\w-_]+)\.vue$/);

  each(fields.keys(), key => {
    const name = key.replace(/^\.\//, '').replace(/\.vue/, '');
    fieldComponents[name] = fields(key).default;
  });

  export default {
    data() {
      const versions = [];
      for (const version in schema) versions.push(version);

      const selectedVersion = sessionStorage.getItem('selectedVersion') || versions[0];

      return {
        model: {},
        displayAdvanced: false,
        selectedVersion,
        versions,
        type: ''
      };
    },
    computed: {
      schema() {
        return schema[this.selectedVersion][this.type] || {};
      }
    },
    methods: {
      updateModel(value, field) {
        this.model[field] = value;
      },
      downloadJSON() {
        if (!this.validateForm()) return;

        const json = this.processModelToJSON(this.model);
        const text = JSON.stringify(json, null, 2);

        this.downloadText(text, this.filename);
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

        const fields = document.getElementsByClassName('error');
        if (!fields.length) return form.checkValidity();

        clearTimeout(this.shakeTimeout);
        each(fields, field => { field.classList.add('shake'); });
        this.shakeTimeout = setTimeout(() => { each(fields, field => { field.classList.remove('shake'); }); }, 500);
        return false;
      },
      processModelToJSON(model) {
        return model;
      }
    },
    watch: {
      selectedVersion(version) {
        sessionStorage.setItem('selectedVersion', version);
      }
    },
    components: fieldComponents
  };
</script>

<style lang="scss">
    .form-item:last-child {
        margin-bottom: 0;
    }
</style>
