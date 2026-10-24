// Accessible printing support for clinical health summary export
document.addEventListener('DOMContentLoaded', function() {
  var printBtn = document.getElementById('btn-print');
  if (printBtn) {
    printBtn.addEventListener('click', function() {
      window.print();
    });
  }
});
