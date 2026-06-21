// Rich-text editor interop for the Blazor admin (Interactive Server).
//
// CKEditor mutates the DOM it is attached to, which conflicts with Blazor's diffing. The Razor
// RichTextEditor component therefore renders an opaque host element that Blazor never re-renders,
// and this module owns the CKEditor instance entirely. Content flows back to .NET via a change
// callback (so the bound model stays current without Blazor touching the editor DOM).

// The license key is supplied server-side via a meta tag (from CKEditor:LicenseKey config).
function configuredLicenseKey() {
	const meta = document.querySelector('meta[name="ckeditor-license-key"]');
	return meta && meta.content ? meta.content : '';
}

// CKEditor 5 distribution channels gate the license key: a configured key (commercial/development)
// is valid for the official cloud CDN, whereas 'GPL' is only valid for the npm distribution. So load
// from the cloud CDN when a key is configured, and fall back to esm.sh (npm, self-contained browser
// ESM) + 'GPL' when none is - keeping the editor working for keyless/open-source consumers.
function ckeditorSource(hasKey) {
	return hasKey
		? 'https://cdn.ckeditor.com/ckeditor5/46.1.1/ckeditor5.js'
		: 'https://esm.sh/ckeditor5@46.1.1';
}

export async function init(element, dotnetRef, initialValue) {
	const key = configuredLicenseKey();
	const {
		ClassicEditor,
		Essentials, Bold, Italic, Underline, Strikethrough, Code, BlockQuote,
		Heading, Link, List, Indent, IndentBlock, Paragraph,
		Font, FontSize, FontFamily, FontColor, FontBackgroundColor, Alignment,
		Table, TableToolbar, CodeBlock, HorizontalLine, SpecialCharacters,
		RemoveFormat, Highlight, SourceEditing, HtmlEmbed, GeneralHtmlSupport
	} = await import(ckeditorSource(key !== ''));

	const editor = await ClassicEditor.create(element, {
		licenseKey: key || 'GPL',
		initialData: initialValue || '',
		plugins: [
			Essentials, Bold, Italic, Underline, Strikethrough, Code, BlockQuote,
			Heading, Link, List, Indent, IndentBlock, Paragraph,
			Font, FontSize, FontFamily, FontColor, FontBackgroundColor, Alignment,
			Table, TableToolbar, CodeBlock, HorizontalLine, SpecialCharacters,
			RemoveFormat, Highlight, SourceEditing, HtmlEmbed, GeneralHtmlSupport
		],
		toolbar: [
			'htmlEmbed',
			'undo', 'redo', '|',
			'heading', '|',
			'bold', 'italic', 'underline', 'strikethrough', 'removeFormat', '|',
			'fontSize', 'fontFamily', 'fontColor', 'fontBackgroundColor', 'highlight', '|',
			'link', 'bulletedList', 'numberedList', 'indent', '|',
			'blockQuote', 'code', 'codeBlock', '|',
			'insertTable', 'horizontalLine', '|',
			'specialCharacters', '|',
			'alignment', '|',
			'sourceEditing'
		],
		table: {
			contentToolbar: ['tableColumn', 'tableRow', 'mergeTableCells']
		},
		htmlSupport: {
			allow: [{ name: /.*/, attributes: true, classes: true, styles: true }]
		}
	});

	// Push edits back to the bound .NET model.
	editor.model.document.on('change:data', () => {
		dotnetRef.invokeMethodAsync('OnContentChanged', editor.getData());
	});

	// Returned to Blazor as an IJSObjectReference so the component can tear the editor down.
	return {
		destroy: () => editor.destroy()
	};
}
